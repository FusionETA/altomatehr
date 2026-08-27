using AltomateHR.Api.Modules.Holidays;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Shifts;

namespace AltomateHR.Api.Modules.Overtime;

// Resolves WHICH OT multiplier applies for an employee on a given date.
// Deliberately stops short of computing pay: that needs an hourly rate (HRP)
// and an ordinary rate (ORP), neither of which exist in this backend yet —
// there's no payroll module. This answers the "which rate" half so the payroll
// pass only has to supply the money.
//
// Precedence: public holiday > rest day > normal day. A holiday falling on a
// rest day pays the holiday premium (the higher one).
public class OtRateService : IOtRateService
{
    // Mon–Fri, matching HoursSummaryService's fallback when no shift defines
    // working days.
    private static readonly int[] DefaultWorkingDays = [1, 2, 3, 4, 5];

    private readonly IPolicyService _policies;
    private readonly IShiftService _shifts;
    private readonly IHolidayService _holidays;

    public OtRateService(IPolicyService policies, IShiftService shifts, IHolidayService holidays)
    {
        _policies = policies;
        _shifts = shifts;
        _holidays = holidays;
    }

    public async Task<OtRateResolution> ResolveAsync(string employeeId, DateTime date, string? projectId)
    {
        var policy = await _policies.GetEffectivePolicyAsync(employeeId);
        var dayType = await ResolveDayTypeAsync(employeeId, date, projectId);

        if (policy is null)
            return new OtRateResolution(dayType, null, null, "No policy resolved for this employee.");

        if (!policy.OtEnabled)
            return new OtRateResolution(dayType, null, null, "OT is disabled on this employee's policy.");

        if (policy.OtMethod != OtMethod.CASH)
            return new OtRateResolution(dayType, null, null,
                $"OT is banked as time off ({policy.OtMethod}), so no cash multiplier applies.");

        return dayType switch
        {
            OtDayType.PUBLIC_HOLIDAY => new OtRateResolution(
                dayType, policy.OtRatePublicHoliday, policy.OtRatePublicHolidayInShift,
                "Public holiday."),
            OtDayType.REST_DAY => new OtRateResolution(
                dayType, policy.OtRateRestDay, policy.OtRateRestDayInShift,
                "Rest day (outside the employee's working days)."),
            _ => new OtRateResolution(
                dayType, policy.OtRateNormalDay, null,
                "Normal working day — in-shift hours are ordinary pay."),
        };
    }

    private async Task<OtDayType> ResolveDayTypeAsync(string employeeId, DateTime date, string? projectId)
    {
        if (await _holidays.IsHolidayAsync(date, projectId)) return OtDayType.PUBLIC_HOLIDAY;

        var shift = await _shifts.GetEffectiveShiftAsync(employeeId);
        var workingDays = ParseWorkingDays(shift?.WorkingDays);
        return workingDays.Contains(IsoDayOfWeek(date)) ? OtDayType.NORMAL_DAY : OtDayType.REST_DAY;
    }

    // Comma-separated ISO weekday numbers ("1,2,3,4,5"). Empty/unset means the
    // Mon–Fri default rather than "no working days at all".
    private static HashSet<int> ParseWorkingDays(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [.. DefaultWorkingDays];

        var days = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.TryParse(part, out var d) ? d : 0)
            .Where(d => d is >= 1 and <= 7)
            .ToHashSet();

        return days.Count == 0 ? [.. DefaultWorkingDays] : days;
    }

    // 1 = Monday … 7 = Sunday (matches Shift.WorkingDays storage).
    private static int IsoDayOfWeek(DateTime date) =>
        date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
}
