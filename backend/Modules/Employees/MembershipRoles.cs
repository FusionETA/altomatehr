using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

// A membership answers two separate questions:
//
//   · admin access — Role: Admin or Owner administer the company (unchanged);
//   · employment   — does this person WORK here: paid by payroll, on teams,
//                    under a policy, with an employee portal?
//
// An Employee or Supervisor works here by definition. An Admin or Owner works
// here only when EmploymentRole says so ("Employee & admin"); otherwise they
// are admin only, as every admin was before the two were split.
//
// Anything that asks "who are the staff" — payroll rosters, headcounts, leave,
// hours, team supervisors — goes through here, not through Role.
public static class MembershipRoles
{
    // "Employee", "Supervisor", or null when they don't work here.
    public static string? EmploymentRole(this OrganizationMembership m) =>
        OrgRoles.IsOnPayroll(m.Role) ? m.Role : Normalize(m.EmploymentRole);

    public static bool IsEmployed(this OrganizationMembership m) => m.EmploymentRole() is not null;

    // Both an admin and on the staff: the people who get the portal switch.
    public static bool IsEmployeeAndAdmin(this OrganizationMembership m) =>
        OrgRoles.IsAdministrative(m.Role) && m.EmploymentRole() is not null;

    // The role a session acting in the EMPLOYEE portal carries — their
    // employment role. Null for someone admin only.
    public static string? EmployeePortalRole(this OrganizationMembership m) => m.EmploymentRole();

    // Change what someone can administer. Moving an Employee/Supervisor up to
    // Admin or Owner keeps them on the staff (their employment moves into
    // EmploymentRole); moving an admin down to Employee/Supervisor makes that
    // their only role. Every Role write goes through here so the two never
    // disagree.
    public static void SetRole(this OrganizationMembership m, string role)
    {
        if (OrgRoles.IsAdministrative(role))
        {
            if (!OrgRoles.IsAdministrative(m.Role)) m.EmploymentRole = Normalize(m.Role);
        }
        else
        {
            m.EmploymentRole = null;
        }
        m.Role = role;
    }

    private static string? Normalize(string? role) =>
        string.Equals(role, OrgRoles.Employee, StringComparison.OrdinalIgnoreCase) ? OrgRoles.Employee
        : string.Equals(role, OrgRoles.Supervisor, StringComparison.OrdinalIgnoreCase) ? OrgRoles.Supervisor
        : null;
}
