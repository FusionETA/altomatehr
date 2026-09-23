using AltomateHR.Api.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AltomateHR.Api.Modules.Policies;

// Endpoint gate for a module the employee's POLICY may switch off.
//
// Distinct from Organizations' [RequireModule], which gates on the ORG'S PLAN:
// that one asks whether the company pays for claims at all, this one asks
// whether this particular employee's policy includes it. Both can apply.
//
// The policy editor has had these checkboxes all along and nothing ever read
// them: a part-time policy with Claims and Leave unticked saved happily, and
// the employee kept full use of both. The checkbox was decoration.
//
// Applied to an employee's OWN use of a module — filing a claim, applying for
// leave, reading their own history. Deliberately NOT applied to approval
// queues: who may decide someone else's request is a question of role and team
// position, and a supervisor whose own policy excludes claims must still be
// able to approve their team's.
//
// Administrative seats pass. An admin's access is their role, and an org whose
// default policy happens to exclude a module should not lose the screens that
// configure it.
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirePolicyModuleAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _module;

    public RequirePolicyModuleAttribute(string module) => _module = module;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var currentUser = services.GetRequiredService<ICurrentUser>();

        if (currentUser.UserId is not { Length: > 0 } userId
            || OrgRoles.IsAdministrative(currentUser.Role))
        {
            await next();
            return;
        }

        var access = await services.GetRequiredService<IPolicyService>()
            .GetModuleAccessAsync(userId);

        if (!access.Allows(_module))
        {
            // 403 rather than 404: the module exists, this person's policy just
            // doesn't include it, and saying so is what lets them ask an admin
            // for the right policy instead of reporting a broken screen.
            context.Result = new ObjectResult(new
            {
                message = $"Your employee policy doesn't include {_module}. "
                        + "Ask an admin if you need access.",
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
