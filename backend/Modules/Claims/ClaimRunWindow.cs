namespace AltomateHR.Api.Modules.Claims;

// The submission window covered by one month's claims run.
//
// The rule the cutoff setting describes: a claim submitted ON or BEFORE the
// cutoff day belongs to that month's run; one submitted after it still goes
// through, but rolls into the next run. So the run labelled 2026-09 with a
// cutoff of 25 covers 26 Aug through 25 Sep inclusive — it is NOT the calendar
// month, and treating it as one is how a late claim gets paid in the wrong run.
//
// Extracted because two places need exactly this arithmetic — the payroll
// reimbursement export and the dashboard's upcoming-run card — and an off-by-one
// between them would have them disagree about which claims are in the run.
public readonly record struct ClaimRunWindow(DateTime From, DateTime To, string Label)
{
    // The day the run closes, for the run this window belongs to.
    public DateTime CutoffDate => To.AddDays(-1);

    // `month` is "yyyy-MM". Null means the run currently open: the one whose
    // cutoff has not passed yet.
    public static ClaimRunWindow For(string? month, int cutoffDay, DateTime now)
    {
        var day = Math.Clamp(cutoffDay, 1, 28);
        var anchor = ParseMonth(month) ?? CurrentRunMonth(day, now);

        // Inclusive of the previous month's cutoff + 1 day, exclusive of this
        // month's cutoff + 1 day — so a claim submitted ON the cutoff is in.
        var previousCutoff = new DateTime(anchor.Year, anchor.Month, day).AddMonths(-1);
        var cutoff = new DateTime(anchor.Year, anchor.Month, day);

        return new ClaimRunWindow(
            previousCutoff.Date.AddDays(1),
            cutoff.Date.AddDays(1),
            $"{anchor:yyyy-MM}");
    }

    // Before or on the cutoff, this month's run is still open; after it, the
    // open run is next month's.
    private static DateTime CurrentRunMonth(int cutoffDay, DateTime now) =>
        now.Day <= cutoffDay
            ? new DateTime(now.Year, now.Month, 1)
            : new DateTime(now.Year, now.Month, 1).AddMonths(1);

    private static DateTime? ParseMonth(string? month) =>
        DateTime.TryParseExact(
            month, "yyyy-MM", null, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
}
