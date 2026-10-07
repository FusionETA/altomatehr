namespace AltomateHR.Api.Modules.Payroll;

// When one employee's own Form EA for a year is final.
//
// The form declares the whole January–December year, so — as with the
// admin's bulk EA — it is ready only once all twelve monthly runs are
// approved. That holds for leavers too: someone who leaves can come back the
// same year (a rehire, a transfer back), and a form issued when they left
// would then under-declare that employer's year.
public sealed record EaYear(int ApprovedMonths, bool Ready)
{
    public static EaYear For(IReadOnlyCollection<int> submittedMonths)
    {
        var approved = Enumerable.Range(1, 12).Count(submittedMonths.Contains);
        return new EaYear(approved, approved == 12);
    }

    // Why the form is not ready yet, for whoever is looking (the employee or HR).
    public static string NotReadyReason(int year) =>
        $"The {year} EA form will be ready once all 12 months of {year} payroll are approved.";
}
