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

    public const string Refusal =
        "You can't change your own employee record from the admin portal. Ask another admin or the owner.";

    public static async Task<bool> RefusesAsync(ActionExecutingContext context, IEnumerable<string> routeKeys)
    {
        if (AccessGateIsRead(context)) return false;

        var services = context.HttpContext.RequestServices;
        var currentUser = services.GetRequiredService<ICurrentUser>();
        if (currentUser.UserId is not { } me
            || currentUser.OrganizationId is not { } orgId
            || currentUser.IsSupport
            || !string.Equals(currentUser.Role, OrgRoles.Admin, StringComparison.OrdinalIgnoreCase))
            return false;

        var ids = routeKeys
            .Select(k => context.RouteData.Values.TryGetValue(k, out var raw) ? raw?.ToString() : null)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();
        if (ids.Count == 0) return false;

        var directory = services.GetRequiredService<IDirectoryService>();
        var mine = ids.Contains(me, StringComparer.Ordinal);
        if (!mine)
        {
            var myProfiles = (await directory.GetProfilesByUserAsync(me))
                .Where(p => p.OrganizationId == orgId)
                .Select(p => p.Id);
            mine = myProfiles.Any(p => ids.Contains(p, StringComparer.Ordinal));
        }
        if (!mine) return false;

        return (await directory.GetMembershipForUserAsync(me))?.IsEmployed() == true;
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
