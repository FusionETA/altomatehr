namespace AltomateHR.Api.Modules.Payroll;

// When an employee's own Form EA for a year is final.
//
// The form declares their whole year with this company, so every month of it
// must be approved first — a form issued earlier would under-declare. For most
// people that is January–December, the same as the admin's bulk EA. Someone
// who left during the year has no pay after they left, so their year ends
// with their leaving month and they need not wait for December.
//
// Two guards keep a leaver's early form from changing later:
//   • pay approved AFTER the leaving month (a late final payout) extends the
//     year to that month, and
//   • any run in the year that holds a payslip of theirs and is not yet
//     approved means pay is still coming, so the year is not final.
public sealed record EaYear(int ThroughMonth, int ApprovedMonths, bool Ready)
{
    public static EaYear For(
        int year,
        DateTime? leaveDate,
        int lastPaidMonth,
        IReadOnlyCollection<int> submittedMonths,
        bool hasUnsubmittedPay)
    {
        var through = leaveDate is { } left && left.Year <= year
            ? Math.Max(left.Year == year ? left.Month : 1, lastPaidMonth)
            : 12;

        var approved = Enumerable.Range(1, through).Count(submittedMonths.Contains);

        return new EaYear(through, approved, approved == through && !hasUnsubmittedPay);
    }
}
