namespace AltomateHR.Api.Modules.Payroll;

// The monthly submission files: KWSP's EPF CSV, PERKESO's combined SOCSO/EIS
// (/SKBBK) TXT, and LHDN's CP39 PCB TXT.
//
// All three read the run's PAYSLIPS for the money — those are the filed
// figures and never move — and the employees' CURRENT profiles for the
// identifiers, so a corrected EPF number reaches the next regeneration.
public interface IStatutoryFileService
{
    Task<StatutoryFileResult> RenderEpfCsvAsync(string runId);

    Task<StatutoryFileResult> RenderPerkesoTxtAsync(string runId);

    Task<StatutoryFileResult> RenderPcbTxtAsync(string runId);

    // What the files above will need but the run does not yet have. Surfaced
    // on the run page, and enforced before a submission is accepted.
    Task<PayrollRunReadiness.Result?> GetReadinessAsync(string runId);

    // ---- Documents ----

    // One employee's payslip.
    Task<StatutoryFileResult> RenderPayslipPdfAsync(string runId, string employeeProfileId);

    // Every payslip on the run, as a ZIP of one PDF per employee. Finance
    // teams forward these individually, so a single concatenated PDF would
    // have to be split before it was usable.
    Task<StatutoryFileResult> RenderAllPayslipsZipAsync(string runId);

    // The run on one sheet: gross, deductions and net per employee, totalled.
    Task<StatutoryFileResult> RenderSummaryPdfAsync(string runId);

    // Who is paid what, into which account — the sheet an approver checks
    // against the bank file before releasing it.
    Task<StatutoryFileResult> RenderPaymentSchedulePdfAsync(string runId);

    // The LHDN MTD §E worksheet — one page per employee showing the whole PCB
    // calculation, so an employee or an officer can re-derive the deduction by
    // hand. Reads the payslip's stored breakdown; never recomputes it.
    Task<StatutoryFileResult> RenderPcbDetailsPdfAsync(string runId);

    // The bank disbursement file itself. `paymentDate` is the value date the
    // bank should act on; null means the last day of the payroll period.
    Task<StatutoryFileResult> RenderBankFileAsync(string runId, DateTime? paymentDate);
}
