namespace AltomateHR.Api.Common;

// Which employees the CURRENT request may see — the admin's "Employees in
// scope" limit (OrganizationMembership.PolicyScope), resolved to concrete ids.
//
// Unlimited (the default, and for everyone but a policy-limited Admin): both
// lists are null and nothing is filtered. Limited: AppDbContext's query
// filters keep every employee-keyed row (profiles, payslips, claims, leave,
// attendance, overtime, loans, salary history …) to these people, so a list,
// a total or a lookup by id can't reach anyone else — the same way the tenant
// filter keeps a request inside its org.
//
// Filled once per request, before any controller runs, by
// EmployeeScopeMiddleware. Background jobs never set it.
public interface IEmployeeScope
{
    // User ids in scope (always including the admin themselves, so their own
    // claims and leave still work). Null = unlimited.
    List<string>? UserIds { get; }

    // The same people's EmployeeProfile ids, for profile-keyed rows.
    List<string>? ProfileIds { get; }

    bool IsLimited => UserIds is not null;

    // Whether a user id OR a profile id is in scope. Always true when unlimited.
    bool Contains(string id) =>
        UserIds is null || UserIds.Contains(id) || (ProfileIds?.Contains(id) ?? false);
}

public sealed class EmployeeScope : IEmployeeScope
{
    public List<string>? UserIds { get; private set; }
    public List<string>? ProfileIds { get; private set; }

    public void Limit(List<string> userIds, List<string> profileIds)
    {
        UserIds = userIds;
        ProfileIds = profileIds;
    }
}
