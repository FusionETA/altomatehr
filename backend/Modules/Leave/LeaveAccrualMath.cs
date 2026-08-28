using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave;

// The leave arithmetic, kept pure and separate from data access so the rules are
// readable and directly testable. Ported from production's
// modules/leave/domain/accrual.ts.
public static class LeaveAccrualMath
{
    private const int MonthsPerYear = 12;

    private static readonly int[] DefaultWorkingDays = [1, 2, 3, 4, 5];   // Mon-Fri

    // "1,2,3,4,5" → {Mon..Fri}. Null, blank or unparseable falls back to
    // Mon-Fri. Same shape and default as the attendance module's parser, so
    // the two never disagree about what a working week is.
    public static HashSet<int> ParseWorkingDays(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [.. DefaultWorkingDays];

        var days = csv.Split(',')
            .Select(p => int.TryParse(p.Trim(), out var n) ? n : 0)
            .Where(n => n is >= 1 and <= 7)
            .ToHashSet();

        return days.Count == 0 ? [.. DefaultWorkingDays] : days;
    }

    // 1 = Monday … 7 = Sunday, matching how WorkingDays is stored.
    public static int IsoWeekday(DateTime date) =>
        date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;

    // How many days a request actually costs. Ported from production's
    // computeTotalDays: half-days are always 0.5, and a full-day range counts
    // only working weekdays that aren't public holidays. This is why a
    // Friday-to-Monday request costs 2 days rather than 4.
    public static double ComputeTotalDays(
        DateTime startDate,
        DateTime endDate,
        LeaveDuration duration,
        IReadOnlySet<int> workingDays,
        IReadOnlySet<DateTime> holidays)
    {
        if (duration != LeaveDuration.FULL_DAY) return 0.5;

        var start = startDate.Date;
        var end = endDate.Date;
        if (end < start) return 0;

        var days = 0;
        for (var cursor = start; cursor <= end; cursor = cursor.AddDays(1))
        {
            if (!workingDays.Contains(IsoWeekday(cursor))) continue;
            if (holidays.Contains(cursor)) continue;   // production skips these too
            days++;
        }
        return days;
    }

    // One month's accrual, never past the full entitlement.
    public static double NextAccruedDays(double entitledDays, double accruedDays) =>
        Math.Min(entitledDays, accruedDays + entitledDays / MonthsPerYear);

    // The bucket available for THIS year: everything up front for LUMP_SUM,
    // only what has accrued so far for PRO_RATED.
    public static double CurrentBucket(LeaveAccrualMethod method, double entitledDays, double accruedDays) =>
        method == LeaveAccrualMethod.PRO_RATED ? accruedDays : entitledDays;

    // Days the employee can apply for RIGHT NOW: this year's available bucket
    // plus any carry that hasn't lapsed, minus what they've taken.
    public static double AvailableDays(
        LeaveAccrualMethod method,
        double entitledDays,
        double accruedDays,
        double carriedDays,
        bool carriedExpired,
        double usedDays)
    {
        var carry = carriedExpired ? 0 : carriedDays;
        return Math.Max(0, CurrentBucket(method, entitledDays, accruedDays) + carry - usedDays);
    }

    // How much rolls into next year, capped by the type's ceiling.
    public static double CarryForwardAmount(
        LeaveAccrualMethod method,
        double entitledDays,
        double accruedDays,
        double carriedDays,
        double usedDays,
        double? maxCarryForwardDays)
    {
        var remaining = Math.Max(0, CurrentBucket(method, entitledDays, accruedDays) + carriedDays - usedDays);
        return maxCarryForwardDays is null
            ? remaining
            : Math.Min(remaining, Math.Max(0, maxCarryForwardDays.Value));
    }

    // Carried days not yet consumed at expiry time. The current-year bucket is
    // assumed to be spent FIRST — otherwise an employee would always be racing
    // the clock against their carried days.
    public static double UnusedCarriedAtExpiry(
        LeaveAccrualMethod method,
        double entitledDays,
        double accruedDays,
        double carriedDays,
        double usedDays)
    {
        var usedFromCarry = Math.Max(0, usedDays - CurrentBucket(method, entitledDays, accruedDays));
        return Math.Max(0, carriedDays - usedFromCarry);
    }
}
