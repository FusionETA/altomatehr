namespace AltomateHR.Api.Modules.Payroll;

// When a company's year is final enough for its year-end forms — one
// employee's Form EA, and the admin's bulk EA / CP8D / Form E.
//
// The forms declare what THIS company paid in the year, so they wait until
// every month the company ran payroll for is approved, through December:
// from its FIRST run of the year (any status — a draft counts, so a draft
// January still holds the year back) to December. A company that only
// started running payroll here in March, or that an employee was transferred
// into in July, owes no January run and can still issue its forms.
//
// It holds for leavers too: someone who leaves can come back the same year (a
// rehire, a transfer back), and a form issued when they left would then
// under-declare that employer's year. So everyone waits for December.
public sealed record EaYear(int ApprovedMonths, int RequiredMonths, int? FirstMonth, bool Ready)
{
    // submittedMonths: months with an APPROVED run. runMonths: months with a
    // run in ANY status (drafts included) — only used to find where the
    // company's year starts. Omitted = the approved months alone.
    public static EaYear For(IReadOnlyCollection<int> submittedMonths, IReadOnlyCollection<int>? runMonths = null)
    {
        var first = submittedMonths.Concat(runMonths ?? [])
            .Where(m => m is >= 1 and <= 12)
            .DefaultIfEmpty(0)
            .Min();

        if (first == 0) return new EaYear(0, 12, null, false);

        var required = Enumerable.Range(first, 13 - first).ToList();
        var approved = required.Count(submittedMonths.Contains);
        return new EaYear(approved, required.Count, first, approved == required.Count);
    }

    // The months still to approve, in order.
    public static IReadOnlyList<int> Missing(IReadOnlyCollection<int> submittedMonths, IReadOnlyCollection<int>? runMonths = null)
    {
        var ea = For(submittedMonths, runMonths);
        var first = ea.FirstMonth ?? 1;
        return [.. Enumerable.Range(first, 13 - first).Where(m => !submittedMonths.Contains(m))];
    }

    // Why the form is not ready yet, for whoever is looking (the employee or HR).
    public static string NotReadyReason(int year) =>
        $"The {year} EA form will be ready once this company's {year} payroll is approved through December.";
}
