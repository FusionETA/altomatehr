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

    // The bank disbursement file itself, in the layout of the company's own
    // payroll bank. `paymentDate` is the value date the bank should act on;
    // null means the last day of the payroll period.
    //
    // The last two are needed only by Hong Leong, which publishes TWO upload
    // channels taking different files and requires a statement reference that
    // no payroll data implies. Every other bank ignores them.
    // Everything a finalised run produces, in one zip: the bank file, the
    // summary, the payslips and the three statutory uploads.
    //
    // For an integration that wants the month's output in one request rather
    // than six. A document that cannot be rendered — a bank file with no payor
    // account, say — is SKIPPED rather than failing the bundle, and named in
    // `Skipped` so the caller knows what is absent instead of guessing from a
    // short file list.
    Task<PayrollBundleResult> RenderRunBundleAsync(string runId, DateTime? paymentDate);

    Task<StatutoryFileResult> RenderBankFileAsync(
        string runId,
        DateTime? paymentDate,
        string? recipientReference = null,
        HlbChannel? channel = null);
}

// A zip, plus what did not make it in and why. Ok=false with Error → the whole
// bundle was refused (run not approved, or not found); a bundle that merely
// lost a document is still Ok.
public record PayrollBundleResult(
    bool Ok,
    string? FileName,
    byte[]? Content,
    IReadOnlyList<string> Included,
    IReadOnlyDictionary<string, string> Skipped,
    string? Error);
