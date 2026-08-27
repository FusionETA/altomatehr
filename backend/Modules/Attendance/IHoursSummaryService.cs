using AltomateHR.Api.Modules.Attendance.Dtos;

namespace AltomateHR.Api.Modules.Attendance;

public interface IHoursSummaryService
{
    // The caller's own totals for [from, to].
    Task<HoursBucketsDto> GetMyHoursSummaryAsync(string employeeId, DateTime from, DateTime to);

    // Org-wide, one row per Employee/Supervisor membership (Admin/Owner accounts
    // excluded), optionally narrowed to one team's members.
    Task<HoursSummaryDto> GetOrgHoursSummaryAsync(DateTime from, DateTime to, string? teamId);

    // One employee's totals, for an admin/supervisor reviewing them. Null means
    // "not authorized" (self or their approver only) — caller maps to 403.
    Task<HoursBucketsDto?> GetEmployeeHoursSummaryAsync(
        string employeeId, DateTime from, DateTime to, string requestingUserId, string? requestingRole);
}
