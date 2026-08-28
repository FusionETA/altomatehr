using AltomateHR.Api.Modules.ApiKeys;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AltomateHR.Api.Modules.Partners;

// Least-privilege guardrail for partner tokens (registered globally).
//
// A partner principal carries Role=Admin only so it can clear the existing
// [Authorize(Roles="Admin,Owner")] gates — but the spec promises a partner token
// can do NOTHING beyond its granted, read-only scopes. So for a partner caller we
// flip the posture to DENY-BY-DEFAULT: the request is allowed only if the action
// declares a [RequireScope] whose scope the token holds. Every other endpoint —
// including any Admin-gated write with no scope — is 403 for partner tokens.
//
// wp_live_ keys keep their broader "Admin-in-org, scopes opt-in" posture; this
// only tightens partner tokens.
public sealed class PartnerAccessFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        var isPartner = user.HasClaim(c => c.Type == PartnerAuthenticationDefaults.ClientIdClaim);

        if (isPartner)
        {
            var declaredScopes = context.Filters.OfType<RequireScopeAttribute>()
                .Select(a => a.Scope)
                .ToList();
            var heldScopes = user.FindAll(ApiKeyAuthenticationDefaults.ScopeClaim)
                .Select(c => c.Value)
                .ToHashSet(StringComparer.Ordinal);

            if (declaredScopes.Count == 0 || !declaredScopes.Any(heldScopes.Contains))
            {
                context.Result = new ObjectResult(new
                {
                    error = new { status = 403, message = "This endpoint is not available to partner tokens." },
                })
                { StatusCode = StatusCodes.Status403Forbidden };
                return;
            }
        }

        await next();
    }
}
