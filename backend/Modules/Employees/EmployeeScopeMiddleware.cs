using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Policies;

namespace AltomateHR.Api.Modules.Employees;

// Resolves a policy-limited admin's "Employees in scope" into the people (and
// their profile ids) this request may see, BEFORE any controller or query
// runs — see IEmployeeScope. Everyone else passes straight through.
//
// Done up front rather than lazily: the database filter reads the lists while
// a query is executing, and working them out then would need a second query on
// the same DbContext mid-flight.
//
// An employee's policy is their own, or the org's default when they have none
// (the same resolution payroll uses), so "everyone on Monthly Workers"
// includes people who are on it by default.
public class EmployeeScopeMiddleware
{
    private readonly RequestDelegate _next;

    public EmployeeScopeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        IModuleAccessService access,
        IDirectoryService directory,
        IPolicyService policies,
        EmployeeScope scope)
    {
        // Only a signed-in Admin can be limited; skip the lookup for everyone else.
        if (currentUser.IsAuthenticated
            && currentUser.UserId is { } adminId
            && string.Equals(currentUser.Role, OrgRoles.Admin, StringComparison.OrdinalIgnoreCase)
            && (await access.GetAccessAsync()).PolicyScope is { } policyIds)
        {
            var allowedPolicies = policyIds.ToHashSet(StringComparer.Ordinal);
            var members = await directory.GetMembershipsForCurrentOrgAsync();
            var effective = await policies.GetEffectivePoliciesForEmployeesAsync(members.Select(m => m.UserId));

            // Staff only: a scope covers the people on those policies, never
            // the company's admins or owner (who resolve to the default policy).
            var userIds = members
                .Where(m => OrgRoles.IsOnPayroll(m.Role))
                .Where(m => effective.TryGetValue(m.UserId, out var p) && allowedPolicies.Contains(p.Id))
                .Select(m => m.UserId)
                .Append(adminId)   // their own claims, leave and clock-ins still work
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var inScope = userIds.ToHashSet(StringComparer.Ordinal);
            var profileIds = (await directory.GetProfilesForCurrentOrgAsync())
                .Where(p => inScope.Contains(p.UserId))
                .Select(p => p.Id)
                .ToList();

            scope.Limit(userIds, profileIds);
        }

        await _next(context);
    }
}
