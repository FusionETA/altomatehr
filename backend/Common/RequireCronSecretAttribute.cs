using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace AltomateHR.Api.Common;

// Gate for scheduled jobs. There is no user and no org, so [Authorize] can't
// apply — the caller proves itself with a shared secret instead:
//
//   curl -X POST https://<host>/leave/cron/monthly-accrual \
//     -H "Authorization: Bearer $CRON_SECRET"
//
// Mirrors production's cron routes. The secret lives in user-secrets/config as
// `Cron:Secret`, never in appsettings.json. If it isn't configured the endpoint
// fails CLOSED with 500 rather than running unauthenticated.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireCronSecretAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var expected = context.HttpContext.RequestServices
            .GetRequiredService<IConfiguration>()["Cron:Secret"]?.Trim();

        if (string.IsNullOrEmpty(expected))
        {
            context.Result = new ObjectResult(
                new { ok = false, error = "Cron:Secret is not configured on the server." })
            { StatusCode = StatusCodes.Status500InternalServerError };
            return;
        }

        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        var supplied = header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? header[prefix.Length..].Trim()
            : null;

        if (supplied is null || !FixedTimeEquals(supplied, expected))
        {
            context.Result = new UnauthorizedObjectResult(new { ok = false, error = "unauthorized" });
            return;
        }

        await next();
    }

    // Constant-time compare so a wrong secret can't be recovered by timing.
    private static bool FixedTimeEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a),
            System.Text.Encoding.UTF8.GetBytes(b));
}
