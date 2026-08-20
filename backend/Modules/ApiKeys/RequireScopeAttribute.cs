using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AltomateHR.Api.Modules.ApiKeys;

// Opt-in endpoint gate: require a wp_live_ key to carry a specific scope. Apply ON TOP
// of the normal [Authorize], e.g. [RequireScope("employees:read")].
//
// Scopes only narrow MACHINE access below the key's Admin role — human (JWT) callers
// are unaffected and always pass through. Existing endpoints stay untouched until you
// choose to add this attribute.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequireScopeAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _scope;

    public RequireScopeAttribute(string scope) => _scope = scope;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var isApiKey = user.HasClaim(c => c.Type == ApiKeyAuthenticationDefaults.ApiKeyIdClaim);

        if (isApiKey && !user.HasClaim(ApiKeyAuthenticationDefaults.ScopeClaim, _scope))
        {
            context.Result = new ObjectResult(
                new { error = new { status = 403, message = $"API key is missing required scope: {_scope}." } })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
