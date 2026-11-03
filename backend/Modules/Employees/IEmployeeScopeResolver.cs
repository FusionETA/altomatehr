using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

// Turns an admin's "Employees in scope" — the policy ids the Owner ticked in
// Manage access (OrganizationMembership.PolicyScope) — into the concrete
// people it covers, in a NAMED org.
//
// One implementation, two callers, deliberately: EmployeeScopeMiddleware fills
// IEmployeeScope for the current request, and the master-key access endpoint
// answers the same question about a company nobody is signed into. Having them
// resolve it separately is how ABPay and AltomateHR would come to disagree
// about who an admin may see.
public interface IEmployeeScopeResolver
{
    // `adminUserId` is always included — an admin's own claims, leave and
    // clock-ins stay reachable, and GET /employees shows them to themselves.
    Task<ScopedEmployees> ResolveAsync(
        string organizationId, string adminUserId, IReadOnlyList<string> policyIds);
}

// The people a limited admin covers. `People` pairs each one with their
// EmployeeProfile id — null for a member who has no profile row yet. Such a
// person still appears on the payroll roster, under a blank stand-in profile
// whose id is freshly generated each time it is asked for, so the null here
// is the honest answer and `UserId` is what they are matched by.
public sealed record ScopedEmployees(IReadOnlyList<ScopedEmployee> People)
{
    public List<string> UserIds => People.Select(p => p.UserId).ToList();

    public List<string> ProfileIds =>
        People.Where(p => p.EmployeeProfileId is not null).Select(p => p.EmployeeProfileId!).ToList();
}

public sealed record ScopedEmployee(string UserId, string? EmployeeProfileId);
