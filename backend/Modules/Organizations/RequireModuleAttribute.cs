using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace AltomateHR.Api.Modules.Organizations;

// Endpoint/controller gate: the caller's org (and, for admins, their grant) must include
// this module. Unlike [RequireScope] (machine-only), this applies to EVERYONE — a human
// on a FREE plan and a wp_live key in a FREE org are both blocked from a paid module.
// Apply on top of [Authorize], e.g. [RequireModule("claims")] on the controller.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequireModuleAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _module;

    public RequireModuleAttribute(string module) => _module = module;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var access = context.HttpContext.RequestServices.GetRequiredService<IModuleAccessService>();
        var enabled = await access.GetEnabledModulesAsync();

        if (!enabled.Contains(_module, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult(new
            {
                error = new { status = 403, message = $"This organization's plan does not include the '{_module}' module." }
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        await next();
    }
}
