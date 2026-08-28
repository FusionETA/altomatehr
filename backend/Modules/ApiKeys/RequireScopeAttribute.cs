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

    // The scope this attribute demands — read by PartnerAccessFilter to discover
    // which scopes an endpoint declares.
    public string Scope => _scope;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;

        // Scoped machine callers: wp_live_ keys (apikey_id) and partner apps
        // (partner_client — mirrors PartnerAuthenticationDefaults.ClientIdClaim,
        // kept as a literal so this module doesn't depend on Partners). Both carry
        // granted scopes as ScopeClaim; human (JWT) callers have neither and pass.
        var isScopedMachine =
            user.HasClaim(c => c.Type == ApiKeyAuthenticationDefaults.ApiKeyIdClaim) ||
            user.HasClaim(c => c.Type == "partner_client");

        if (isScopedMachine && !user.HasClaim(ApiKeyAuthenticationDefaults.ScopeClaim, _scope))
        {
            context.Result = new ObjectResult(
                new { error = new { status = 403, message = $"Caller is missing required scope: {_scope}." } })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return;
        }

        await next();
    }
}
