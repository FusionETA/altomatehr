using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

// One adjustment category, as the client needs it to render a picker.
//
// The catalogue is served rather than duplicated in the frontend because the
// calculator DISPATCHES on these codes: a second copy that drifted would let
// an admin pick a category whose statutory treatment the UI describes wrongly,
// or one the calculator skips silently. Same reason a call site must read
// PayrollAdjustmentCategories instead of inlining a SubjectTo* decision.
public class PayrollAdjustmentCategoryDto
{
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public PayslipLineKind Kind { get; set; }

    // Which wage bases this row feeds. Shown on the form so an admin can see
    // WHY two allowances of the same size move the tax differently.
    public bool SubjectToEpf { get; set; }
    public bool SubjectToSocso { get; set; }
    public bool SubjectToEis { get; set; }
    public bool SubjectToPcb { get; set; }
    public bool SubjectToHrdf { get; set; }

    // The annual ringgit ceiling under which the row is PCB-exempt, or on a
    // TP1 deduction the per-item yearly cap.
    public decimal? TaxExemptLimit { get; set; }

    public bool ReducesBase { get; set; }
    public bool ReducesGross { get; set; }
    public bool CashNeutral { get; set; }
    public bool FeedsLp1Relief { get; set; }
    public bool AddsToCp38Field { get; set; }
    public bool IsAdditionalRemuneration { get; set; }
    public bool OffsetsPcb { get; set; }

    // A benefit in kind: the employee never receives cash, so it stays out of
    // gross and net while still being taxable income on Form EA.
    public bool NonCash { get; set; }

    // Which heading the picker files it under.
    public string Group { get; set; } = string.Empty;
}

// A profile fixed allowance, with the index the override dictionary keys on.
public class FixedAllowanceRowDto
{
    // The array position in the profile's JSON. Overrides are keyed by this as
    // a string, so it has to travel with the row.
    public int Index { get; set; }
    public string Category { get; set; } = string.Empty;
    public string? Name { get; set; }
    public decimal Amount { get; set; }
    public bool TreatAsRecurring { get; set; }
}

// A loan installment falling in this period. Read-only here — the loan is
// edited on the Loans page, and the deduction is applied by generation.
public class LoanInstallmentPreviewDto
{
    public string LoanId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

// Everything the adjustment editor needs for one employee on one run, in one
// read. Five round trips to assemble a single form is how a screen ends up
// rendering half-populated.
public class PayrollAdjustmentContextDto
{
    public string EmployeeProfileId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public SalaryType SalaryType { get; set; }

    // Null when nothing has been typed for this employee on this run.
    public PayrollRunAdjustmentDto? Adjustment { get; set; }

    public IReadOnlyList<FixedAllowanceRowDto> FixedAllowances { get; set; } = [];

    // What attendance derived for the period, so the form can show it as the
    // placeholder an empty override falls back to.
    public decimal? AutoWorkedHours { get; set; }
    public decimal? AutoExpectedHours { get; set; }

    // False when the employee's policy keeps them off attendance. The figures
    // above are then absent rather than zero — for HOURLY staff a confident
    // zero is a zero payslip.
    public bool AttendanceApplies { get; set; }

    // False when the policy banks overtime as time off or disables it. Typed
    // OT hours are then IGNORED by generation, so the form must say so rather
    // than accept a number that silently does nothing.
    public bool CashOvertime { get; set; }
    public string? OvertimeDisabledReason { get; set; }

    public IReadOnlyList<LoanInstallmentPreviewDto> LoanInstallments { get; set; } = [];

    // Only a DRAFT run accepts an edit.
    public bool Editable { get; set; }
}
