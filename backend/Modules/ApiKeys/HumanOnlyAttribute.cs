using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AltomateHR.Api.Modules.ApiKeys;

// Refuses every machine caller — wp_live_ company keys and partner tokens — and
// lets signed-in people through. For admin tools no integration has any business
// using, where no scope would make sense.
//
// A wp_live_ key acts as an Admin of its company and scopes only narrow the
// endpoints that opt in with [RequireScope]; anything without one is open to
// every key. On the API-keys screen that meant a key granted only
// "employees:read" could mint itself a new key with every scope, so scopes
// could not actually hold anyone back. Xero (disconnect, re-sync the chart) and
// the audit log are in the same position.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HumanOnlyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var isMachine =
            user.HasClaim(c => c.Type == ApiKeyAuthenticationDefaults.ApiKeyIdClaim) ||
            user.HasClaim(c => c.Type == "partner_client");

        if (isMachine)
        {
            context.Result = new ObjectResult(
                new { error = new { status = 403, message = "This endpoint is not available to API keys." } })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
