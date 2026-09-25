using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace AltomateHR.Api.Modules.Organizations;

// Endpoint/controller gate for a module. Two checks, and which one applies is
// read off the endpoint itself:
//
//   · Every endpoint needs the ORG's plan to include the module. A human on a
//     FREE plan and a wp_live key in a FREE org are both blocked from a paid
//     module. (Unlike [RequireScope], which is machine-only.)
//
//   · A ROLE-RESTRICTED endpoint — any [Authorize(Roles = ...)] on it or its
//     controller, i.e. an admin or supervisor surface — also needs the caller's
//     admin grant to include it. That is the Owner's "Manage access" choice.
//
// The split is what lets one attribute sit on a controller that mixes the two.
// Filing a claim has no role restriction, so an Admin whose grant leaves out
// Claims still files their own; approving every claim in the org does, so that
// Admin cannot. Checking the grant everywhere (as this used to) locked such an
// Admin out of their own claims and clock-ins.
//
// Apply on top of [Authorize], e.g. [RequireModule("claims")] on the controller.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireModuleAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _module;

    public RequireModuleAttribute(string module) => _module = module;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var access = context.HttpContext.RequestServices.GetRequiredService<IModuleAccessService>();

        var ceiling = await access.GetOrgModulesAsync();
        if (!ceiling.Contains(_module, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = Forbidden($"This organization's plan does not include the '{_module}' module.");
            return;
        }

        if (NeedsAdminGrant(context))
        {
            var enabled = await access.GetEnabledModulesAsync();
            if (!enabled.Contains(_module, StringComparer.OrdinalIgnoreCase))
            {
                context.Result = Forbidden(
                    $"Your admin access doesn't include the '{_module}' module. Ask the organization's owner to grant it.");
                return;
            }
        }

        await next();
    }

    private static bool NeedsAdminGrant(ActionExecutingContext context)
    {
        var metadata = context.ActionDescriptor.EndpointMetadata;
        if (metadata.OfType<ModuleGrantExemptAttribute>().Any()) return false;

        return metadata.OfType<AuthorizeAttribute>().Any(a => !string.IsNullOrWhiteSpace(a.Roles));
    }

    private static ObjectResult Forbidden(string message) =>
        new(new { error = new { status = 403, message } }) { StatusCode = StatusCodes.Status403Forbidden };
}

// An admin endpoint that other modules' screens read as a LOOKUP — the employee
// and team lists behind the claims and attendance filters, the bank register
// behind the employee form. Gating those by their own module broke every screen
// that borrows them, so they are held to the org's plan only. Keep this to plain
// lists: anything with salaries, ICs or bank details stays behind its grant.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ModuleGrantExemptAttribute : Attribute;
