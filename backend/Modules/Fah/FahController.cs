using System.Text;
using System.Text.Json;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Fah.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AltomateHR.Api.Modules.Fah;

// FAH, the in-app assistant. Admins and owners, signed in as themselves —
// never a key (FAH makes its own short-lived key per turn).
[ApiController]
[Route("fah")]
[Authorize(Roles = "Admin,Owner")]
[HumanOnly]
public class FahController : ControllerBase
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IFahService _fah;

    public FahController(IFahService fah) => _fah = fah;

    // GET /fah/status — whether FAH is set up here, so the button only shows when it works.
    [HttpGet("status")]
    public IActionResult Status() => Ok(_fah.Status());

    // GET /fah/conversations — the caller's own chats, newest first.
    [HttpGet("conversations")]
    public async Task<IActionResult> List() => Ok(await _fah.ListAsync());

    // GET /fah/conversations/{id} — one of them, as the panel shows it.
    [HttpGet("conversations/{id}")]
    public async Task<IActionResult> Get(string id) =>
        await _fah.GetAsync(id) is { } conversation ? Ok(conversation) : NotFound();

    // POST /fah/chat — one message. Streams server-sent events: "progress" as
    // FAH checks things, then one "result" (the answer) or "error".
    [HttpPost("chat")]
    [EnableRateLimiting("fah")]
    public Task Chat(FahChatRequestDto dto) =>
        StreamAsync(progress => _fah.ChatAsync(dto, progress, CancellationToken.None));

    // POST /fah/confirm — the admin's Confirm or Cancel on a change FAH is
    // holding. Streams the same way as /chat.
    [HttpPost("confirm")]
    [EnableRateLimiting("fah")]
    public Task Confirm(FahConfirmRequestDto dto) =>
        StreamAsync(progress => _fah.ConfirmAsync(dto, progress, CancellationToken.None));

    // The turn runs to the end even if the admin closes the panel: stopping
    // between a confirmed change and saving the chat would lose the record of
    // it. So the request's abort token is not passed in, and a write to a
    // closed connection is simply dropped.
    //
    // The ping every 10 seconds keeps the proxy from cutting a long turn off
    // as idle, the same as the realtime stream.
    private async Task StreamAsync(Func<Func<FahProgressEvent, Task>, Task<FahResult>> run)
    {
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-transform";
        Response.Headers["X-Accel-Buffering"] = "no";

        var gate = new SemaphoreSlim(1, 1);
        async Task WriteAsync(string frame)
        {
            await gate.WaitAsync();
            try
            {
                if (HttpContext.RequestAborted.IsCancellationRequested) return;
                await Response.Body.WriteAsync(Encoding.UTF8.GetBytes(frame));
                await Response.Body.FlushAsync();
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // The admin closed the panel; the turn carries on regardless.
            }
            finally
            {
                gate.Release();
            }
        }

        Task SendAsync(string type, object data) =>
            WriteAsync($"event: {type}\ndata: {JsonSerializer.Serialize(data, Json)}\n\n");

        await WriteAsync(": connected\n\n");

        using var stopPings = new CancellationTokenSource();
        var pings = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            try
            {
                while (await timer.WaitForNextTickAsync(stopPings.Token))
                    await WriteAsync(": ping\n\n");
            }
            catch (OperationCanceledException)
            {
            }
        });

        FahResult result;
        try
        {
            result = await run(e => SendAsync("progress", e));
        }
        finally
        {
            await stopPings.CancelAsync();
            await pings;
        }

        if (result.Ok)
            await SendAsync("result", result.Response!);
        else
            await SendAsync("error", new
            {
                status = result.NotFound ? 404 : result.Unavailable ? 503 : 400,
                error = result.NotFound ? "That chat wasn't found." : result.Error,
            });
    }
}
