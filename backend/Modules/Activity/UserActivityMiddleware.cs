using Stopwatch = System.Diagnostics.Stopwatch;
using System.Security.Claims;
using AltomateHR.Api.Modules.Activity.Entities;
using AltomateHR.Api.Modules.ApiMonitoring;
using AltomateHR.Api.Modules.Auth;
using Microsoft.AspNetCore.Diagnostics;

namespace AltomateHR.Api.Modules.Activity;

// Records every change a signed-in PERSON makes — each POST/PUT/PATCH/DELETE —
// as an "Action" in their activity session. This is the server's own account of
// what happened, so unlike the browser tracker's page views and clicks it cannot
// be skipped or faked by the client.
//
// Only requests carrying an activity session ("asid") are recorded, which by
// construction means people: API keys and partner apps never have one. Reads are
// not recorded — a screen fires several, and the page view already says what
// was looked at.
//
// Registered just inside ApiRequestLogMiddleware and OUTSIDE the exception
// handler, so the status recorded is the one the person actually got. Like that
// middleware it must never cost a request anything: a stopwatch and a
// non-blocking queue write, with any failure logged and dropped.
public class UserActivityMiddleware
{
    // The tracker's own batches, and the sign-in plumbing the session already
    // records (start on login, end on logout, touch on refresh).
    private static readonly string[] SkippedPrefixes =
        ["/activity", "/auth/login", "/auth/refresh", "/auth/logout", "/realtime"];

    private static long _dropped;

    private readonly RequestDelegate _next;
    private readonly IActivityEventQueue _queue;
    private readonly ILogger<UserActivityMiddleware> _logger;

    public UserActivityMiddleware(
        RequestDelegate next, IActivityEventQueue queue, ILogger<UserActivityMiddleware> logger)
    {
        _next = next;
        _queue = queue;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsWrite(context.Request.Method)
            || SkippedPrefixes.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var startedAt = DateTime.UtcNow;
        var started = Stopwatch.GetTimestamp();
        var escaped = false;
        try
        {
            await _next(context);
        }
        catch
        {
            escaped = true;
            throw;
        }
        finally
        {
            Record(context, startedAt, Stopwatch.GetElapsedTime(started), escaped);
        }
    }

    public static bool IsWrite(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method)
        || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private void Record(HttpContext context, DateTime startedAt, TimeSpan elapsed, bool escaped)
    {
        try
        {
            var row = Build(context, startedAt, elapsed, escaped);
            if (row is null) return;

            if (!_queue.TryEnqueue(row) && Interlocked.Increment(ref _dropped) % 1000 == 1)
                _logger.LogWarning("Activity queue is full; dropping events ({Dropped} so far).", _dropped);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record an activity event.");
        }
    }

    public static UserActivityEvent? Build(HttpContext context, DateTime startedAt, TimeSpan elapsed, bool escaped)
    {
        var user = context.User;
        var sessionId = user.FindFirstValue(TokenService.SessionClaim);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(userId)) return null;

        // The exception handler clears the endpoint and route values before
        // re-running the pipeline; its feature keeps the ones that threw.
        var handled = context.Features.Get<IExceptionHandlerFeature>();
        var route = ApiRequestLogMiddleware.RouteTemplate(context.GetEndpoint() ?? handled?.Endpoint);
        if (route == "(unmatched)") return null;

        var routeValues = context.Request.RouteValues.Count > 0
            ? context.Request.RouteValues
            : handled?.RouteValues;

        return new UserActivityEvent
        {
            SessionId = Truncate(sessionId, 40)!,
            UserId = Truncate(userId, 40)!,
            OrganizationId = Truncate(user.FindFirstValue("org"), 40),
            Kind = ActivityKind.Action,
            Name = Truncate($"{context.Request.Method.ToUpperInvariant()} {route}", 160)!,
            TargetId = Truncate(TargetIdOf(routeValues), 40),
            StatusCode = escaped ? StatusCodes.Status500InternalServerError : context.Response.StatusCode,
            DurationMs = (int)Math.Min(int.MaxValue, Math.Round(elapsed.TotalMilliseconds)),
            IsSupport = user.FindFirstValue(TokenService.SupportClaim) == "1",
            CreatedAt = startedAt,
        };
    }

    // The record the request was about: the {id} route value, or failing that
    // the first {somethingId}. "claims/{id}/approve" → the claim's id.
    public static string? TargetIdOf(RouteValueDictionary? values)
    {
        if (values is null) return null;
        if (values.TryGetValue("id", out var id) && id is string s && s.Length > 0) return s;

        foreach (var (key, value) in values)
        {
            if (key.EndsWith("Id", StringComparison.OrdinalIgnoreCase) && value is string v && v.Length > 0)
                return v;
        }
        return null;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
