using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AltomateHR.Api.Modules.Employees;

// An Admin who also works here (Employee & admin) can't change their OWN
// employee record from the admin portal: their salary, payroll adjustments,
// leave entitlements, leave filed on their behalf, team approvers, transfers.
// Someone else does that — otherwise HR could raise their own pay.
//
// Reads pass (their own record is theirs to see), and so does everything they
// do as themselves in the employee portal, where the session's role is their
// employment role and this never applies. The Owner is exempt: a small
// company's owner on its own payroll has nobody else to set it. So is
// support. An admin who is admin only has no employee record to protect.
//
// Registered globally for the employee route keys every controller uses
// (employeeId, userId, employeeProfileId); [OwnRecordGuard("id")] adds others
// — the employee record's own "{id}" routes.
public static class OwnRecordGuard
{
    public static readonly string[] EmployeeRouteKeys = ["employeeId", "userId", "employeeProfileId"];

    public const string Refusal = OwnRecordRule.Refusal;

    public static async Task<bool> RefusesAsync(ActionExecutingContext context, IEnumerable<string> routeKeys)
    {
        if (AccessGateIsRead(context)) return false;

        var ids = routeKeys
            .Select(k => context.RouteData.Values.TryGetValue(k, out var raw) ? raw?.ToString() : null)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();
        if (ids.Count == 0) return false;

        // A route value may be a user id or a profile id; ask both ways.
        var rule = context.HttpContext.RequestServices.GetRequiredService<IOwnRecordRule>();
        foreach (var id in ids)
            if (await rule.IsCallersOwnAsync(id, id)) return true;
        return false;
    }

    private static bool AccessGateIsRead(ActionExecutingContext context)
    {
        var method = context.HttpContext.Request.Method;
        return HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
               || context.ActionDescriptor.EndpointMetadata.OfType<ReadOnlyActionAttribute>().Any();
    }

    public static ObjectResult Forbidden() =>
        new(new { error = new { status = 403, message = Refusal } }) { StatusCode = StatusCodes.Status403Forbidden };
}

public sealed class OwnRecordRouteFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (await OwnRecordGuard.RefusesAsync(context, OwnRecordGuard.EmployeeRouteKeys))
        {
            context.Result = OwnRecordGuard.Forbidden();
            return;
        }
        await next();
    }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class OwnRecordGuardAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _routeKey;
    public OwnRecordGuardAttribute(string routeKey) => _routeKey = routeKey;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (await OwnRecordGuard.RefusesAsync(context, [_routeKey]))
        {
            context.Result = OwnRecordGuard.Forbidden();
            return;
        }
        await next();
    }
}
