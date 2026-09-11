using AltomateHR.Api.Modules.Attendance.Dtos;

namespace AltomateHR.Api.Modules.Attendance;

public interface IHoursSummaryService
{
    // The caller's own totals for [from, to].
    Task<HoursBucketsDto> GetMyHoursSummaryAsync(string employeeId, DateTime from, DateTime to);

    // Org-wide, one row per Employee/Supervisor membership (Admin/Owner accounts
    // excluded), optionally narrowed to one team's members.
    Task<HoursSummaryDto> GetOrgHoursSummaryAsync(DateTime from, DateTime to, string? teamId);

    // Totals for an EXPLICIT set of employees, keyed by user id.
    //
    // Separate from GetOrgHoursSummaryAsync because that one is a reporting
    // view: it decides its own roster and drops Admin/Owner accounts. Payroll
    // pays whoever has an employment record, role included, so it has to name
    // the roster itself — silently returning no hours for an admin who is also
    // on the payroll would understate an hourly employee's pay.
    Task<IReadOnlyDictionary<string, HoursBucketsDto>> GetHoursForEmployeesAsync(
        IEnumerable<string> employeeIds, DateTime from, DateTime to);

    // One employee's totals, for an admin/supervisor reviewing them. Null means
    // "not authorized" (self or their approver only) — caller maps to 403.
    Task<HoursBucketsDto?> GetEmployeeHoursSummaryAsync(
        string employeeId, DateTime from, DateTime to, string requestingUserId, string? requestingRole);
}
