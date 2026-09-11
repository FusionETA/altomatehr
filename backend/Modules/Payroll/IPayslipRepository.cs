using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// This calendar year's locked-in figures for one employee, as LHDN's formula
// wants them. Only SUBMITTED runs count — a draft is not yet tax withheld.
public sealed record PayrollYtdTotals
{
    // Y — PCB-taxable pay: prorated pay + overtime + the taxable portion of
    // PCB-subject allowances. The taxable PORTION matters: an allowance that was
    // exempt under its annual ceiling must not come back as taxable next month.
    public decimal Taxable { get; init; }

    // K — employee EPF already contributed.
    public decimal Epf { get; init; }

    // X — PCB already withheld. CP38 is deliberately excluded: the MTD spec's X
    // does not include tax instalments, so counting it would suppress this
    // month's withholding.
    public decimal Pcb { get; init; }

    // Z — zakat already paid, which offsets the annual tax outright.
    public decimal Zakat { get; init; }

    // Employee SOCSO + SKBBK + EIS, for the shared RM 350/year relief.
    public decimal SocsoEis { get; init; }

    // ΣLP — TP1 declarations already relieved this year.
    public decimal AllowableDeductions { get; init; }

    // Per-category totals, so this year's annual exemption ceilings pick up
    // where the last run left off instead of resetting every month.
    public IReadOnlyDictionary<string, decimal> AllowanceByCategory { get; init; } =
        new Dictionary<string, decimal>();
}

public interface IPayslipRepository
{
    Task<List<Payslip>> GetForRunAsync(string payrollRunId);

    Task<List<PayslipLineItem>> GetLineItemsForRunAsync(string payrollRunId);

    // One employee's payslips WITH their runs, newest period first, restricted
    // to SUBMITTED runs. A draft is still being edited — showing an employee a
    // figure that may still move is worse than showing them nothing yet.
    //
    // Returns the pair because the period and the submission date live on the
    // run: fetching them separately is one query per month the employee has
    // worked here.
    Task<List<(Payslip Payslip, PayrollRun Run)>> GetForEmployeeAsync(string employeeProfileId);

    // One payslip WITH its run, so the caller can check both ownership and
    // that the run is submitted before returning anything.
    Task<(Payslip Payslip, PayrollRun Run)?> GetWithRunAsync(string payslipId);

    Task<List<PayslipLineItem>> GetLineItemsAsync(string payslipId);

    // Wipe the run's payslips and line items and write these instead. One
    // transaction, because a half-regenerated run is worse than a stale one.
    Task ReplaceForRunAsync(
        string payrollRunId,
        IReadOnlyList<Payslip> payslips,
        IReadOnlyList<PayslipLineItem> lineItems);

    // Every employee's YTD in one pass, keyed by employee profile id. Batched
    // deliberately: a per-employee version would fire six aggregates per head on
    // a run that already loops over the whole org.
    Task<IReadOnlyDictionary<string, PayrollYtdTotals>> GetYtdByEmployeeAsync(
        int year, string? excludeRunId);

    // Year-to-date as a PAYSLIP shows it — gross, net and each contribution
    // split employee/employer — through and including `month`.
    //
    // Different from GetYtdByEmployeeAsync in both shape and scope. That one
    // feeds LHDN's formula and must count only what has actually been filed.
    // This one is display: it counts submitted months AND `includeRunId`, so a
    // payslip previewed from a draft run does not show a year-to-date that is
    // mysteriously missing the very month being read. Once the run is
    // submitted the two views agree, and there is no double counting because
    // the run is named rather than added.
    Task<IReadOnlyDictionary<string, PayslipYtdSummary>> GetYtdThroughPeriodAsync(
        int year, int month, string? includeRunId);
}

public sealed record PayslipYtdSummary
{
    public decimal Gross { get; init; }
    public decimal Net { get; init; }
    public decimal EpfEmployee { get; init; }
    public decimal EpfEmployer { get; init; }
    public decimal SocsoEmployee { get; init; }
    public decimal SocsoEmployer { get; init; }
    public decimal EisEmployee { get; init; }
    public decimal EisEmployer { get; init; }
    public decimal SkbbkEmployee { get; init; }
    public decimal Pcb { get; init; }
    public decimal Hrdf { get; init; }
}
