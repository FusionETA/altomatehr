using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Overtime.Entities;
using AltomateHR.Api.Modules.Shifts;
using AltomateHR.Api.Modules.Shifts.Entities;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Attendance;

// Worked-minutes reporting: buckets clocked time into Normal/Rest-day and
// separately totals approved/pending/rejected OT (submission-driven, never
// derived from clock duration — see HoursBucketsDto). Reduced scope vs. the
// reference app: no public-holiday bucket (no PH model here), no free-text
// employee search filter on the org view.
public class HoursSummaryService : IHoursSummaryService
{
    private static readonly int[] DefaultWorkingDays = [1, 2, 3, 4, 5];
    private const int DefaultStandardDailyMin = 8 * 60;
    private const int DefaultLunchBreakMin = 60;

    private readonly IAttendanceRepository _attendance;
    private readonly IOvertimeRepository _overtime;
    private readonly IShiftRepository _shifts;
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IOrganizationService _organizations;
    private readonly ITeamMembershipRepository _teamMemberships;
    private readonly ISupervisionService _supervision;
    private readonly ICurrentUser _currentUser;

    public HoursSummaryService(
        IAttendanceRepository attendance,
        IOvertimeRepository overtime,
        IShiftRepository shifts,
        IOrganizationMembershipRepository memberships,
        IOrganizationService organizations,
        ITeamMembershipRepository teamMemberships,
        ISupervisionService supervision,
        ICurrentUser currentUser)
    {
        _attendance = attendance;
        _overtime = overtime;
        _shifts = shifts;
        _memberships = memberships;
        _organizations = organizations;
        _teamMemberships = teamMemberships;
        _supervision = supervision;
        _currentUser = currentUser;
    }

    public async Task<HoursBucketsDto> GetMyHoursSummaryAsync(string employeeId, DateTime from, DateTime to)
    {
        var ctx = await BuildContextAsync();
        return await ComputeAsync(employeeId, from, to, ctx);
    }

    public async Task<HoursSummaryDto> GetOrgHoursSummaryAsync(DateTime from, DateTime to, string? teamId)
    {
        var members = await _memberships.GetForCurrentOrgAsync();
        var staff = members.Where(m => m.Role is "Employee" or "Supervisor").ToList();

        if (!string.IsNullOrEmpty(teamId))
        {
            var teamEmployeeIds = (await _teamMemberships.GetByTeamAsync(teamId))
                .Select(tm => tm.EmployeeId).ToHashSet();
            staff = staff.Where(m => teamEmployeeIds.Contains(m.UserId)).ToList();
        }

        var ctx = await BuildContextAsync();
        var emails = await _supervision.GetEmailsAsync(staff.Select(m => m.UserId));
        var employees = new List<EmployeeHoursSummaryDto>();
        foreach (var member in staff)
        {
            var buckets = await ComputeAsync(member.UserId, from, to, ctx);
            employees.Add(new EmployeeHoursSummaryDto
            {
                EmployeeId = member.UserId,
                Email = emails.GetValueOrDefault(member.UserId),
                Buckets = buckets,
            });
        }

        return new HoursSummaryDto { Totals = Sum(employees.Select(e => e.Buckets)), Employees = employees };
    }

    public async Task<HoursBucketsDto?> GetEmployeeHoursSummaryAsync(
        string employeeId, DateTime from, DateTime to, string requestingUserId, string? requestingRole)
    {
        var authorized = requestingUserId == employeeId
            || await _supervision.CanApproveAsync(employeeId, requestingUserId, requestingRole);
        if (!authorized) return null;

        var ctx = await BuildContextAsync();
        return await ComputeAsync(employeeId, from, to, ctx);
    }

    // Shared, range-independent lookups (org working hours) fetched once per call.
    private async Task<(string? Start, string? End)> BuildContextAsync()
    {
        if (string.IsNullOrEmpty(_currentUser.OrganizationId)) return (null, null);
        var org = await _organizations.GetByIdAsync(_currentUser.OrganizationId);
        return (org?.WorkingHoursStart, org?.WorkingHoursEnd);
    }

    private async Task<HoursBucketsDto> ComputeAsync(
        string employeeId, DateTime from, DateTime to, (string? Start, string? End) orgHours)
    {
        var (workingDays, standardDailyMin) = await ResolveScheduleAsync(employeeId, orgHours);

        var records = (await _attendance.GetByEmployeeAsync(employeeId))
            .Where(r => r.Date >= from.Date && r.Date <= to.Date && r.DurationMin is not null)
            .ToList();

        var buckets = new HoursBucketsDto();
        foreach (var record in records)
        {
            var minutes = record.DurationMin!.Value;
            buckets.TotalMin += minutes;
            if (workingDays.Contains(IsoWeekday(record.Date))) buckets.NormalMin += minutes;
            else buckets.RestDayMin += minutes;
        }

        var otRequests = (await _overtime.GetByEmployeeAsync(employeeId))
            .Where(o => o.WorkDate.Date >= from.Date && o.WorkDate.Date <= to.Date);
        foreach (var ot in otRequests)
        {
            switch (ot.Status)
            {
                case OvertimeStatus.APPROVED: buckets.OtApprovedMin += ot.RequestedMinutes; break;
                case OvertimeStatus.PENDING: buckets.OtPendingMin += ot.RequestedMinutes; break;
                case OvertimeStatus.REJECTED: buckets.OtRejectedMin += ot.RequestedMinutes; break;
                // CANCELLED excluded — never counted toward any bucket.
            }
        }

        buckets.ExpectedMin = ExpectedMinutesForRange(from, to, workingDays, standardDailyMin);
        return buckets;
    }

    // Assigned Shift (via the employee's org membership) wins; otherwise the
    // org's WorkingHoursStart/End. Reduced scope vs. the reference app: no
    // "project's default shift" middle tier — an employee with no Shift
    // assigned falls straight through to org hours.
    private async Task<(HashSet<int> WorkingDays, int StandardDailyMin)> ResolveScheduleAsync(
        string employeeId, (string? Start, string? End) orgHours)
    {
        var membership = await _memberships.GetForUserInCurrentOrgAsync(employeeId);
        Shift? shift = membership?.ShiftId is not null ? await _shifts.GetByIdAsync(membership.ShiftId) : null;

        if (shift is not null)
        {
            return (ParseWorkingDays(shift.WorkingDays), StandardDailyMinutesFrom(
                shift.StartTime, shift.EndTime, shift.LunchBreakMinutes));
        }

        return (new HashSet<int>(DefaultWorkingDays),
            StandardDailyMinutesFrom(orgHours.Start, orgHours.End, DefaultLunchBreakMin));
    }

    private static HashSet<int> ParseWorkingDays(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new HashSet<int>(DefaultWorkingDays);
        var days = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .Where(n => n is >= 1 and <= 7)
            .ToHashSet();
        return days.Count == 0 ? new HashSet<int>(DefaultWorkingDays) : days;
    }

    private static int StandardDailyMinutesFrom(string? start, string? end, int lunchBreakMin)
    {
        if (!TryParseHm(start, out var startMin) || !TryParseHm(end, out var endMin) || endMin <= startMin)
            return DefaultStandardDailyMin;
        return Math.Max(0, endMin - startMin - Math.Max(0, lunchBreakMin));
    }

    private static bool TryParseHm(string? value, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var parts = value.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m))
            return false;
        if (h is < 0 or > 24 || m is < 0 or > 59) return false;
        minutes = h * 60 + m;
        return true;
    }

    // 1 = Monday … 7 = Sunday (matches Shift.WorkingDays storage).
    private static int IsoWeekday(DateTime date) => date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    // Scheduled days × daily minutes for an inclusive [from, to] range. Leave/
    // holidays aren't subtracted — this is a pure schedule target, matching
    // the reference app's expectedMinutesForRange.
    private static int ExpectedMinutesForRange(DateTime from, DateTime to, HashSet<int> workingDays, int standardDailyMin)
    {
        if (standardDailyMin <= 0 || to.Date < from.Date) return 0;
        var days = 0;
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            if (workingDays.Contains(IsoWeekday(d))) days++;
        return days * standardDailyMin;
    }

    private static HoursBucketsDto Sum(IEnumerable<HoursBucketsDto> all)
    {
        var totals = new HoursBucketsDto();
        foreach (var b in all)
        {
            totals.NormalMin += b.NormalMin;
            totals.RestDayMin += b.RestDayMin;
            totals.TotalMin += b.TotalMin;
            totals.OtApprovedMin += b.OtApprovedMin;
            totals.OtPendingMin += b.OtPendingMin;
            totals.OtRejectedMin += b.OtRejectedMin;
            totals.ExpectedMin += b.ExpectedMin;
        }
        return totals;
    }
}
