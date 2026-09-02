using AltomateHR.Api.Modules.Overtime;

namespace AltomateHR.Api.Modules.Attendance;

// The worked-minutes rule, as pure arithmetic. Kept out of HoursSummaryService
// so it can be tested without ten fakes — same split as LeaveAccrualMath.
//
// The rule, in one line: a working day counts at most the shift length, and
// anything past that needs an approved overtime submission to become money.
public static class AttendanceHoursMath
{
    // What one attendance record contributes to the buckets.
    public readonly record struct DayContribution(
        int NormalMin,
        int RestDayMin,
        int PublicHolidayMin,
        int BeyondShiftMin,
        int BreakMin,
        int WorkedMin);

    /// <param name="clockedMin">Raw clock-out minus clock-in.</param>
    /// <param name="recordedBreakMin">Sum of the day's finished breaks.</param>
    /// <param name="standardDailyMin">The shift length, already net of its unpaid break.</param>
    /// <param name="unpaidBreakMin">The shift's unpaid break, for the no-break fallback.</param>
    public static DayContribution ForDay(
        int clockedMin,
        int recordedBreakMin,
        int standardDailyMin,
        int unpaidBreakMin,
        OtDayType dayType)
    {
        if (clockedMin <= 0) return default;

        // Recorded breaks come off the clock time. When nothing was recorded but
        // the day ran past the shift, fall back to the shift's unpaid break:
        // otherwise an employee who never taps break start/end has their lunch
        // counted as worked time, and the whole calculation is only as good as
        // the least diligent person using it.
        //
        // Only when the day exceeds the shift, so a genuinely short day isn't
        // docked a break that may never have been taken.
        var breakMin = recordedBreakMin > 0
            ? recordedBreakMin
            : clockedMin > standardDailyMin ? unpaidBreakMin : 0;

        breakMin = Math.Clamp(breakMin, 0, clockedMin);
        var worked = clockedMin - breakMin;

        return dayType switch
        {
            // A holiday beats a rest day when one falls on the other, matching
            // IOtRateService. Uncapped: none of it is scheduled time.
            OtDayType.PUBLIC_HOLIDAY =>
                new DayContribution(0, 0, worked, 0, breakMin, worked),

            OtDayType.REST_DAY =>
                new DayContribution(0, worked, 0, 0, breakMin, worked),

            // Capped at the shift length. Clocking in late and out late to make
            // the time up still lands a full day; the overflow is recorded in
            // BeyondShiftMin so the UI can show it without it counting as pay.
            _ => new DayContribution(
                NormalMin: Math.Min(worked, standardDailyMin),
                RestDayMin: 0,
                PublicHolidayMin: 0,
                BeyondShiftMin: Math.Max(0, worked - standardDailyMin),
                BreakMin: breakMin,
                WorkedMin: worked),
        };
    }
}
