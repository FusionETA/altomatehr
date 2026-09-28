using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Payroll is for Employees and Supervisors (OrgRoles.IsOnPayroll), as it was
// in the previous system. Every place that picks people for payroll — the
// roster, the run picker, generation, the imports, loans — reads the org's
// profiles through this rather than on their own, so an admin cannot creep
// back into one list.
public static class PayrollMembers
{
    // The org's profiles minus anyone whose membership is an Admin or Owner. A
    // profile with no membership left (a former member) is kept: whether they
    // are paid is for archive and leave dates to say, not their role.
    public static async Task<List<EmployeeProfile>> PayrollProfilesAsync(this IDirectoryService directory)
    {
        var notOnPayroll = await NotOnPayrollUserIdsAsync(directory);
        return [.. (await directory.GetProfilesForCurrentOrgAsync()).Where(p => !notOnPayroll.Contains(p.UserId))];
    }

    public static async Task<IReadOnlySet<string>> NotOnPayrollUserIdsAsync(this IDirectoryService directory) =>
        (await directory.GetMembershipsForCurrentOrgAsync())
            .Where(m => !OrgRoles.IsOnPayroll(m.Role))
            .Select(m => m.UserId)
            .ToHashSet(StringComparer.Ordinal);
}
