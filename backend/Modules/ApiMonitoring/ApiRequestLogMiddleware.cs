using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiMonitoring.Entities;
using AltomateHR.Api.Modules.Partners;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Routing;

namespace AltomateHR.Api.Modules.ApiMonitoring;

// Times every request and drops one ApiRequestLog row into the queue.
//
// Registered OUTSIDE the exception handler, so the status it records is the one
// the caller actually got (the handler turns some exceptions into a 409), and
// the duration covers the whole pipeline.
//
// It must never cost a request anything: the only work on the request path is
// a stopwatch, a pass-through copy of the first few KB of the response, and a
// non-blocking queue write. Anything that goes wrong here is logged and dropped.
public class ApiRequestLogMiddleware
{
    // Enough to hold any error body this API writes; a 4 MB payslip ZIP is
    // passed straight through, only its first bytes are kept.
    private const int CaptureBytes = 4096;
    private const int MaxMessageLength = 1000;

    // The live notification stream is one request that stays open for hours —
    // its "duration" means nothing. The rest are tooling, not product traffic.
    private static readonly string[] SkippedPrefixes =
        ["/realtime/stream", "/health", "/openapi", "/scalar"];

    private static long _dropped;

    private readonly RequestDelegate _next;
    private readonly IApiRequestLogQueue _queue;
    private readonly ILogger<ApiRequestLogMiddleware> _logger;

    public ApiRequestLogMiddleware(
        RequestDelegate next, IApiRequestLogQueue queue, ILogger<ApiRequestLogMiddleware> logger)
    {
        _next = next;
        _queue = queue;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (ShouldSkip(context.Request))
        {
            await _next(context);
            return;
        }

        var startedAt = DateTime.UtcNow;
        var started = Stopwatch.GetTimestamp();
        var originalBody = context.Response.Body;
        var capture = new CapturingStream(originalBody, CaptureBytes);
        context.Response.Body = capture;

        Exception? escaped = null;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            // Only reaches here when the exception handler couldn't answer —
            // usually because the response had already started.
            escaped = ex;
            throw;
        }
        finally
        {
            context.Response.Body = originalBody;
            Record(context, startedAt, Stopwatch.GetElapsedTime(started), capture, escaped);
        }
    }

    private static bool ShouldSkip(HttpRequest request) =>
        HttpMethods.IsOptions(request.Method)
        || SkippedPrefixes.Any(p => request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    private void Record(
        HttpContext context, DateTime startedAt, TimeSpan elapsed, CapturingStream capture, Exception? escaped)
    {
        try
        {
            var handled = context.Features.Get<IExceptionHandlerFeature>();
            var exception = escaped ?? handled?.Error;
            var status = escaped is not null ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;

            var row = new ApiRequestLog
            {
                CreatedAt = startedAt,
                Method = Truncate(context.Request.Method, 10)!,
                // The exception handler clears the endpoint before re-running
                // the pipeline; its feature keeps the one that threw.
                Route = Truncate(RouteTemplate(context.GetEndpoint() ?? handled?.Endpoint), 300)!,
                StatusCode = status,
                DurationMs = (int)Math.Min(int.MaxValue, Math.Round(elapsed.TotalMilliseconds)),
                OrganizationId = Truncate(context.User.FindFirstValue("org"), 40),
            };

            (row.CallerType, row.CallerId) = Caller(context.User);

            if (status >= 400)
            {
                // A crash's own message says what broke; the body of a 500 is
                // only the handler's generic sentence.
                row.ErrorMessage = Truncate(
                    status >= 500 && exception is not null
                        ? exception.Message
                        : MessageFromBody(capture.Captured, context.Response.ContentType),
                    MaxMessageLength);
            }

            if (exception is not null)
            {
                row.ExceptionType = Truncate(exception.GetType().FullName, 200);
                row.ExceptionSource = Truncate(SourceOf(exception), 300);
            }

            if (!_queue.TryEnqueue(row))
            {
                // Once per thousand, not per row: a full queue means the writer
                // is behind, and a log line per request would make that worse.
                if (Interlocked.Increment(ref _dropped) % 1000 == 1)
                    _logger.LogWarning("API request log queue is full; dropping rows ({Dropped} so far).", _dropped);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record an API request log row.");
        }
    }

    // [Route("[controller]")] fills in the class name ("Organizations/current"),
    // while callers hit "/organizations/current" — so literal segments are
    // lowercased to read like the URL. Parameter names are left alone.
    internal static string RouteTemplate(Endpoint? endpoint)
    {
        if (endpoint is not RouteEndpoint route || route.RoutePattern.RawText is not { Length: > 0 } raw)
            return "(unmatched)";

        var segments = raw.TrimStart('/').Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            if (!segments[i].StartsWith('{')) segments[i] = segments[i].ToLowerInvariant();
        }
        return string.Join('/', segments);
    }

    internal static (string Type, string? Id) Caller(ClaimsPrincipal user)
    {
        var keyId = user.FindFirstValue(ApiKeyAuthenticationDefaults.ApiKeyIdClaim);
        if (keyId is not null) return (ApiCallerTypes.ApiKey, Truncate(keyId, 40));

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.HasClaim(c => c.Type == PartnerAuthenticationDefaults.ClientIdClaim))
            return (ApiCallerTypes.Partner, Truncate(userId, 40));

        return userId is null ? (ApiCallerTypes.Anonymous, null) : (ApiCallerTypes.User, Truncate(userId, 40));
    }

    // The message the caller was shown, in whichever shape this API wrote it:
    // { error: { message } }, { error }, { message }, a ValidationProblem's
    // errors, or a ProblemDetails' detail/title. Anything else: the raw text.
    internal static string? MessageFromBody(ReadOnlySpan<byte> body, string? contentType)
    {
        if (body.IsEmpty) return null;
        if (contentType is null
            || !(contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                 || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(body.ToArray());
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String) return root.GetString();
            if (root.ValueKind != JsonValueKind.Object) return Encoding.UTF8.GetString(body);

            if (root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String) return error.GetString();
                if (error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("message", out var nested)
                    && nested.ValueKind == JsonValueKind.String)
                    return nested.GetString();
            }

            if (StringProperty(root, "message") is { } message) return message;

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    if (field.Value.ValueKind == JsonValueKind.Array
                        && field.Value.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.String } first)
                        return first.GetString();
                }
            }

            return StringProperty(root, "detail") ?? StringProperty(root, "title") ?? Encoding.UTF8.GetString(body);
        }
        catch (JsonException)
        {
            // Plain text, or JSON cut off at the capture limit.
            return Encoding.UTF8.GetString(body);
        }
    }

    private static string? StringProperty(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // The first frame in this app's own code — "PayrollRunService.GenerateAsync",
    // not the framework frame that rethrew it. Async methods compile to a nested
    // state machine (<GenerateAsync>d__12.MoveNext), so that is unwrapped.
    internal static string? SourceOf(Exception exception)
    {
        var frames = new StackTrace(exception, fNeedFileInfo: false).GetFrames();
        foreach (var frame in frames)
        {
            var method = frame.GetMethod();
            var type = method?.DeclaringType;
            if (method is null || type is null) continue;
            if (type.Namespace?.StartsWith("AltomateHR", StringComparison.Ordinal) != true
                && type.DeclaringType?.Namespace?.StartsWith("AltomateHR", StringComparison.Ordinal) != true)
                continue;

            if (type.Name.StartsWith('<') && type.DeclaringType is { } outer)
            {
                var end = type.Name.IndexOf('>');
                var name = end > 1 ? type.Name[1..end] : method.Name;
                return $"{outer.Name}.{name}";
            }

            return $"{type.Name}.{method.Name}";
        }

        return null;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}

// Passes every write straight through and keeps a copy of only the first
// `limit` bytes — enough to read an error message back, without buffering a
// download in memory.
internal sealed class CapturingStream : Stream
{
    private readonly Stream _inner;
    private readonly byte[] _buffer;
    private int _length;

    public CapturingStream(Stream inner, int limit)
    {
        _inner = inner;
        _buffer = new byte[limit];
    }

    public ReadOnlySpan<byte> Captured => _buffer.AsSpan(0, _length);

    private void Keep(ReadOnlySpan<byte> data)
    {
        var room = _buffer.Length - _length;
        if (room <= 0) return;
        var take = Math.Min(room, data.Length);
        data[..take].CopyTo(_buffer.AsSpan(_length));
        _length += take;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Keep(buffer.AsSpan(offset, count));
        _inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Keep(buffer);
        _inner.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Keep(buffer.AsSpan(offset, count));
        return _inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Keep(buffer.Span);
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
