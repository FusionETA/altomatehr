using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Modules.Payroll;

// Bulk-loading a DRAFT run's manual adjustments from a spreadsheet, and the
// optional salary changes that ride alongside them.
//
// The alternative is the per-employee editor, which is right for one or two
// people and miserable for forty.
public interface IPayrollAdjustmentImportService
{
    // The template, pre-filled with the run's payable employees and whatever
    // manual lines they already have.
    Task<TabularExportResult?> BuildTemplateAsync(string runId);

    Task<PayrollAdjustmentImportResult> ImportAsync(string runId, byte[] content, TabularFormat format);
}

// All-or-nothing: on any failure nothing is written, so an admin fixes the file
// and re-uploads rather than reconciling a half-applied import.
//
// `Ok == false` with no Errors and no Message is "no such run".
public sealed class PayrollAdjustmentImportResult
{
    public bool Ok { get; init; }
    public bool Found { get; init; } = true;

    // A whole-file problem: not a draft, unreadable, a missing column.
    public string? Message { get; init; }

    public IReadOnlyList<TabularImportError> Errors { get; init; } = [];

    // What landed, once it did.
    public int EmployeesAffected { get; init; }
    public int LinesWritten { get; init; }

    // Employees on the run the file said nothing about. Not an error — but
    // under replace semantics their existing manual lines are now gone, so it
    // is reported rather than left for them to discover on the payslip.
    public int EmployeesCleared { get; init; }

    public int SalaryChangesApplied { get; init; }

    // Named, not counted: "3 were skipped" sends an admin hunting.
    public IReadOnlyList<string> SalarySkippedNotMonthly { get; init; } = [];
}
