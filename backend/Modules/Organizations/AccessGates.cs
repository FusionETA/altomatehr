using AltomateHR.Api.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AltomateHR.Api.Modules.Organizations;

// The admin-access gates that sit beside [RequireModule]. Each reads the
// caller's AdminAccess (IModuleAccessService.GetAccessAsync), which is full
// for Owners, staff and API keys — so only a limited Admin is ever refused.

// A POST that only READS (a file export, an import preview). A View-only admin
// may still call it; without this every POST counts as a change.
[AttributeUsage(AttributeTargets.Method)]
public sealed class ReadOnlyActionAttribute : Attribute;

internal static class AccessGate
{
    public static bool IsRead(ActionExecutingContext context)
    {
        var method = context.HttpContext.Request.Method;
        return HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
               || context.ActionDescriptor.EndpointMetadata.OfType<ReadOnlyActionAttribute>().Any();
    }

    public static ObjectResult Forbidden(string message) =>
        new(new { error = new { status = 403, message } }) { StatusCode = StatusCodes.Status403Forbidden };
}

// Changing the company's CONFIGURATION — organisation details, claim settings,
// work schedule (shifts, holidays), payroll settings / company info / portal
// logins, the Xero connection. Needs the "Change settings" switch from the
// Owner's Manage access. Reading those pages is not gated here.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSettingsAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!AccessGate.IsRead(context))
        {
            var access = context.HttpContext.RequestServices.GetRequiredService<IModuleAccessService>();
            if (!(await access.GetAccessAsync()).CanChangeSettings)
            {
                context.Result = AccessGate.Forbidden(
                    "Your admin access doesn't include changing settings. Ask the organization's owner.");
                return;
            }
        }

        await next();
    }
}

// Company-wide payroll — creating, generating, submitting, approving or
// reverting a run, and the statutory files and annual forms. These must cover
// EVERY employee, so an admin limited to some policies can only view them.
// Applies to reads too when `IncludeReads` (a file download IS the action).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireFullEmployeeScopeAttribute : Attribute, IAsyncActionFilter
{
    public bool IncludeReads { get; init; }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (IncludeReads || !AccessGate.IsRead(context))
        {
            var access = context.HttpContext.RequestServices.GetRequiredService<IModuleAccessService>();
            if (!(await access.GetAccessAsync()).HasFullEmployeeScope)
            {
                context.Result = AccessGate.Forbidden(
                    "This covers every employee in the company, and your admin access is limited to some policies. Ask an admin with access to all employees.");
                return;
            }
        }

        await next();
    }
}

// The route's employee must be in the caller's scope (IEmployeeScope) — a
// policy-limited admin gets 404 for anyone else, exactly as for someone in
// another company. `routeKey` names the route value holding a user id or an
// employee profile id.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class EmployeeInScopeAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _routeKey;

    public EmployeeInScopeAttribute(string routeKey) => _routeKey = routeKey;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var scope = context.HttpContext.RequestServices.GetRequiredService<IEmployeeScope>();
        if (scope.IsLimited
            && context.RouteData.Values.TryGetValue(_routeKey, out var raw)
            && raw?.ToString() is { Length: > 0 } id
            && !scope.Contains(id))
        {
            context.Result = new NotFoundResult();
            return;
        }

        await next();
    }
}
