using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Employees;

// Fills IEmployeeScope for a policy-limited admin — the people (and profile
// ids) this request may see — BEFORE any controller or query runs. Everyone
// else passes straight through. The resolution itself lives in
// IEmployeeScopeResolver, shared with the master-key access endpoint.
//
// Done up front rather than lazily: the database filter reads the lists while
// a query is executing, and working them out then would need a second query on
// the same DbContext mid-flight.
public class EmployeeScopeMiddleware
{
    private readonly RequestDelegate _next;

    public EmployeeScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        IModuleAccessService access,
        IEmployeeScopeResolver resolver,
        EmployeeScope scope)
    {
        // Only a signed-in Admin can be limited; skip the lookup for everyone else.
        if (currentUser.IsAuthenticated
            && currentUser.UserId is { } adminId
            && currentUser.OrganizationId is { } orgId
            && string.Equals(currentUser.Role, OrgRoles.Admin, StringComparison.OrdinalIgnoreCase)
            && (await access.GetAccessAsync()).PolicyScope is { } policyIds)
        {
            var resolved = await resolver.ResolveAsync(orgId, adminId, policyIds);
            scope.Limit(resolved.UserIds, resolved.ProfileIds);
        }

        await _next(context);
    }
}
