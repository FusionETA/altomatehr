using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Realtime;

// Server-Sent Events: one long-lived HTTP response per browser tab, over which
// the server pushes "this surface changed" nudges.
//
// SSE rather than WebSockets because the traffic is one-directional and tiny —
// the client never sends anything back, so a full duplex upgrade (and its proxy
// and scaling story) buys nothing.
//
// AUTH NOTE: the client connects with `fetch` + a ReadableStream, not the
// browser's `EventSource`. EventSource can't set an Authorization header, which
// would have forced the access token into the query string — where it lands in
// access logs and browser history. This project keeps the access token in
// memory precisely to avoid that, so the transport bends, not the auth model.
[ApiController]
[Route("realtime")]
[Authorize]
public class RealtimeController : ControllerBase
{
    // Proxies and load balancers close connections that go quiet. A comment
    // frame every 25s keeps the connection warm and lets the client notice a
    // dead stream quickly (nginx's default proxy_read_timeout is 60s).
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);

    private readonly IRealtimeService _realtime;

    public RealtimeController(IRealtimeService realtime) => _realtime = realtime;

    // GET /realtime/stream — the event stream for the caller.
    //
    // Returns void (not IActionResult) because the response body is written
    // incrementally: MVC must not buffer it or append anything after us.
    [HttpGet("stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        using var connection = _realtime.Connect();
        if (connection is null)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers.Connection = "keep-alive";
        // nginx buffers proxied responses by default, which would hold events
        // back until the buffer filled — i.e. destroy the whole point.
        Response.Headers["X-Accel-Buffering"] = "no";

        // Flush the headers plus a comment frame right away so the client's
        // "connected" state is real rather than optimistic.
        await WriteAsync(": connected\n\n", cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // Drain everything queued before waiting again — a burst (bulk
                // approve) becomes several frames, not one lost one.
                while (connection.Events.TryRead(out var evt))
                    await WriteAsync($"data: {evt.ToJson()}\n\n", cancellationToken);

                using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                idle.CancelAfter(HeartbeatInterval);

                try
                {
                    // False = the hub completed the channel (we were disconnected).
                    if (!await connection.Events.WaitToReadAsync(idle.Token)) break;
                }
                catch (OperationCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    await WriteAsync(": ping\n\n", cancellationToken);   // heartbeat, not an event
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The client navigated away or closed the tab — the ordinary way a
            // stream ends, not an error worth logging.
        }
    }

    // GET /realtime/status — how many streams this process is holding open.
    // Admin-only: a connection count is a cheap oracle for "who is online".
    [HttpGet("status")]
    [Authorize(Roles = "Admin,Owner")]
    public IActionResult Status() => Ok(new { connections = _realtime.ConnectionCount });

    private async Task WriteAsync(string frame, CancellationToken cancellationToken)
    {
        await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(frame), cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
