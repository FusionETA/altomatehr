namespace AltomateHR.Api.Modules.Payroll;

// When a company's year is final enough for its year-end forms — one
// employee's Form EA, and the admin's bulk EA / CP8D / Form E.
//
// The forms declare what THIS company paid in the year, so they wait until
// every month from January through December is approved. January, not the
// first run here: a company that paid January–June in another system and
// moved here in July still paid those months, and must import them (YTD
// import) — its runs alone can't tell it apart from a new employer.
//
// The one exception is an admin saying payroll here genuinely STARTED later
// (PayrollSettings.PayrollStartYear/Month — a new company, or one staff were
// transferred into): then the year starts at that month. A run earlier than
// it (an import, or a draft) still pulls the start back, and a draft month
// anywhere in the range holds the year back.
//
// It holds for leavers too: someone who leaves can come back the same year (a
// rehire, a transfer back), and a form issued when they left would then
// under-declare that employer's year. So everyone waits for December.
public sealed record EaYear(int ApprovedMonths, int RequiredMonths, int? FirstMonth, bool Ready)
{
    // submittedMonths: months with an APPROVED run. runMonths: months with a
    // run in ANY status (drafts included). startMonth: where the admin says
    // payroll here began this year (1 = January; 13 = it hadn't begun, so
    // only the runs that exist count).
    public static EaYear For(
        IReadOnlyCollection<int> submittedMonths,
        IReadOnlyCollection<int>? runMonths = null,
        int startMonth = 1)
    {
        var first = submittedMonths.Concat(runMonths ?? [])
            .Where(m => m is >= 1 and <= 12)
            .Append(Math.Clamp(startMonth, 1, 13))
            .Min();

        if (first == 13) return new EaYear(0, 0, null, false);

        var required = Enumerable.Range(first, 13 - first).ToList();
        var approved = required.Count(submittedMonths.Contains);
        return new EaYear(approved, required.Count, first, approved == required.Count && approved > 0);
    }

    // The months still to approve, in order.
    public static IReadOnlyList<int> Missing(
        IReadOnlyCollection<int> submittedMonths,
        IReadOnlyCollection<int>? runMonths = null,
        int startMonth = 1)
    {
        var first = For(submittedMonths, runMonths, startMonth).FirstMonth ?? 13;
        return [.. Enumerable.Range(first, 13 - first).Where(m => !submittedMonths.Contains(m))];
    }

    // Why the form is not ready yet, for whoever is looking (the employee or HR).
    public static string NotReadyReason(int year) =>
        $"The {year} EA form will be ready once this company's {year} payroll is approved through December.";

    // RequiredMonths = 0: a year before payroll started here and with no run in
    // it. There is nothing to approve — saying "0/0 approved, missing: " was
    // true but read as a fault.
    public static string NoPayrollReason(int year) =>
        $"This company ran no payroll here in {year}, so there are no {year} year-end forms.";

    public static string ReasonFor(int year, int requiredMonths) =>
        requiredMonths == 0 ? NoPayrollReason(year) : NotReadyReason(year);
}
