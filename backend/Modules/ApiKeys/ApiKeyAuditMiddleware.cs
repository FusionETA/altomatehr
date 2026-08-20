using System.Security.Claims;
using AltomateHR.Api.Modules.ApiKeys.Entities;

namespace AltomateHR.Api.Modules.ApiKeys;

// Writes one audit row (and bumps LastUsedAt) for every request authenticated by a
// wp_live_ key. Runs AFTER the endpoint so it can record the final status code.
// JWT/user requests are ignored (no apikey_id claim). Best-effort: a logging failure
// never affects the response the caller already received.
public class ApiKeyAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuditMiddleware> _logger;

    public ApiKeyAuditMiddleware(RequestDelegate next, ILogger<ApiKeyAuditMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    // IApiKeyRepository is method-injected from the current request scope.
    public async Task InvokeAsync(HttpContext context, IApiKeyRepository keys)
    {
        await _next(context);

        var keyId = context.User.FindFirstValue(ApiKeyAuthenticationDefaults.ApiKeyIdClaim);
        if (keyId is null) return;   // not an api-key request

        try
        {
            await keys.RecordUsageAsync(new ApiKeyAuditLog
            {
                ApiKeyId = keyId,
                Method = context.Request.Method,
                Path = Truncate(context.Request.Path.Value ?? string.Empty, 500),
                StatusCode = context.Response.StatusCode,
                Ip = context.Connection.RemoteIpAddress?.ToString(),
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write API key audit row.");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
