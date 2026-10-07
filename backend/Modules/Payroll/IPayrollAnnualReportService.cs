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

    // One employee's Form EA: the years they were paid in, each saying whether
    // it is final yet (EaYear), and their own page of the bulk form. Shared by
    // the employee's Payslips page and the admin's per-employee LHDN forms.
    Task<IReadOnlyList<Dtos.EmployeeEaFormDto>> GetEmployeeEaYearsAsync(string employeeProfileId);

    // Not found (Error null) when they were not paid that year; refused with
    // the reason when the year is not final yet.
    Task<StatutoryFileResult> RenderEmployeeEaAsync(string employeeProfileId, int year);

    // The CP8D converter: hand-entered rows rather than a year of runs, zipped
    // as the M + P pair. Renders through the same Cp8dTxt the real downloads
    // use, so a converted file and a generated one can't drift apart.
    StatutoryFileResult ConvertCp8d(Dtos.Cp8dConvertRequestDto request);
}
