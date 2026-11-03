using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AltomateHR.Api.Modules.ApiKeys;

// The other way round from [HumanOnly]: refuses every signed-in person and
// lets machine callers through — wp_live_ company keys and partner tokens.
// For integration endpoints that have no screen behind them.
//
// [RequireScope] alone isn't enough there: it lets every person through by
// design (people are held to their role, not to scopes), so an integration
// endpoint that does something only an Owner should — like making someone an
// admin — would be open to every employee's session.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class KeyOnlyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var isMachine =
            user.HasClaim(c => c.Type == ApiKeyAuthenticationDefaults.ApiKeyIdClaim) ||
            user.HasClaim(c => c.Type == "partner_client");

        if (!isMachine)
        {
            context.Result = new ObjectResult(
                new { error = new { status = 403, message = "This endpoint is for API keys only." } })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
