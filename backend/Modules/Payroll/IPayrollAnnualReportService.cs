namespace AltomateHR.Api.Modules.Payroll;

// The year-end filings: Form EA for each employee, and the employer's own
// Form E with its CP8D schedule — as a PDF to read and as the pipe-delimited
// pair LHDN's e-CP8D upload takes.
//
// All four read the year's SUBMITTED runs. A draft month is not remuneration
// that was paid, and a return built on one would be a false declaration.
public interface IPayrollAnnualReportService
{
    // What can be produced, for the downloads page. Static metadata — it does
    // not depend on the year or on what has been run.
    IReadOnlyList<PayrollAnnualReports.Meta> GetAvailable();

    Task<StatutoryFileResult> RenderAsync(PayrollAnnualReportKind kind, int year);

    // The aggregated year, exposed so the annual page can show the figures
    // before anyone downloads anything.
    Task<PayrollAnnualPayload> LoadAsync(int year);
}
