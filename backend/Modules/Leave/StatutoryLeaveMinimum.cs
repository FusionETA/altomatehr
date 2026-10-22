using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave;

// The Employment Act 1955 (Malaysia) minimums for paid annual and sick leave,
// which rise with length of service. Since the 2023 amendments these leave
// provisions apply to every employee, whatever their wage.
//
// A policy or leave type is free to give MORE; it may never give less. So the
// entitlement the app uses is max(configured days, statutory minimum). Pure and
// static so the rule lives in one place and is directly testable.
//
//   s.60E(1) annual leave:              < 2 yrs → 8,  2 to < 5 yrs → 12, 5+ yrs → 16
//   s.60F(1)(aa) paid sick leave (not
//     hospitalised):                    < 2 yrs → 14, 2 to < 5 yrs → 18, 5+ yrs → 22
//
// Hospitalisation (s.60F(1)(bb), 60 days flat), maternity, paternity and the
// rest have no service-based scale here and are left alone.
public static class StatutoryLeaveMinimum
{
    public enum Kind { None, Annual, Sick }

    // The codes the system seeds: LeaveDefaults writes ANNUAL / MEDICAL; the demo
    // seeder (and older databases) use AL / MC for the same two types — see
    // DbSeeder's "two vocabularies" note.
    private static readonly HashSet<string> AnnualCodes = new(StringComparer.OrdinalIgnoreCase)
        { LeaveDefaults.AnnualCode, "AL" };
    private static readonly HashSet<string> SickCodes = new(StringComparer.OrdinalIgnoreCase)
        { "MEDICAL", "MC" };

    public static Kind KindOf(string? code)
    {
        var c = code?.Trim() ?? string.Empty;
        if (AnnualCodes.Contains(c)) return Kind.Annual;
        if (SickCodes.Contains(c)) return Kind.Sick;
        return Kind.None;
    }

    // When service is measured for a leave year: 1 January of that year.
    //
    // Entitlement rows are per CALENDAR year and their EntitledDays is frozen
    // when the row is opened — by the 1 Jan rollover, or lazily on first use
    // (apply / seed / reset) at any point in the year. Measuring at the start of
    // the leave year gives the same figure whichever of those opens the row, so
    // the date it happened to be created never changes someone's entitlement.
    // Someone who crosses 2 or 5 years mid-year moves up the scale the
    // following year.
    public static DateTime ServiceMeasuredAt(int leaveYear) => new(leaveYear, 1, 1);

    // Whole years of continuous service completed by `asOf`. Never negative: a
    // join date after `asOf` (hired later in the year) counts as 0.
    public static int CompletedYears(DateTime joinDate, DateTime asOf)
    {
        var join = joinDate.Date;
        var at = asOf.Date;
        if (join >= at) return 0;
        var years = at.Year - join.Year;
        if (join.AddYears(years) > at) years--;
        return Math.Max(0, years);
    }

    // The Act's minimum for this leave type in this leave year, or null when the
    // floor does not apply — not annual/sick leave, or no join date on file
    // (we can't know their service, so current behaviour is kept).
    //
    // s.60E(1A): in the year someone joins they have under 12 months' service,
    // so their annual leave is only in proportion to the months they complete
    // that year. A PRO_RATED row already accrues from the join date, so the
    // full-year 8 is the right floor to accrue from; a LUMP_SUM row is granted
    // as stored, so its floor is cut to the completed months here. Sick leave
    // (s.60F) has no such rule.
    public static double? MinimumFor(
        string? code, DateTime? joinDate, int leaveYear, LeaveAccrualMethod method)
    {
        var kind = KindOf(code);
        if (kind == Kind.None || joinDate is null) return null;

        var years = CompletedYears(joinDate.Value, ServiceMeasuredAt(leaveYear));
        double full = (kind, years) switch
        {
            (Kind.Annual, < 2) => 8,
            (Kind.Annual, < 5) => 12,
            (Kind.Annual, _) => 16,
            (Kind.Sick, < 2) => 14,
            (Kind.Sick, < 5) => 18,
            _ => 22,
        };
        if (kind == Kind.Annual && method == LeaveAccrualMethod.LUMP_SUM
            && joinDate.Value.Year >= leaveYear)
            return JoinYearShare(full, joinDate.Value, leaveYear);
        return full;
    }

    // Whole months of service completed between joining and 31 December.
    // Joining on the 1st completes that month; any other day starts counting
    // from the next month's same day (joined 15 Mar → 9 months by 14 Dec).
    public static int MonthsCompletedInJoinYear(DateTime joinDate, int leaveYear)
    {
        if (joinDate.Year > leaveYear) return 0;
        if (joinDate.Year < leaveYear) return 12;
        return 12 - joinDate.Month + (joinDate.Day == 1 ? 1 : 0);
    }

    // full × months / 12; the Act disregards under half a day and counts half
    // a day or more as a whole day.
    private static double JoinYearShare(double full, DateTime joinDate, int leaveYear) =>
        Math.Floor(full * MonthsCompletedInJoinYear(joinDate, leaveYear) / 12 + 0.5);

    // The FULL-YEAR entitlement to use: the configured figure (policy override,
    // else the type default, or an admin's per-employee figure), lifted to the
    // statutory minimum when it falls short. Any first-year proration (PRO_RATED
    // accrual from the join date) works on this full-year figure afterwards.
    public static double Apply(
        double configuredDays, string? code, DateTime? joinDate, int leaveYear, LeaveAccrualMethod method) =>
        MinimumFor(code, joinDate, leaveYear, method) is { } min ? Math.Max(configuredDays, min) : configuredDays;

    public static double Apply(
        double configuredDays, LeaveType type, DateTime? joinDate, int leaveYear, LeaveAccrualMethod method) =>
        Apply(configuredDays, type.Code, joinDate, leaveYear, method);
}
