namespace AltomateHR.Api.Modules.Payroll.Entities;

// A one-off allowance, deduction or reimbursement that applies to ONE employee
// on ONE run. Persisted as a JSON array in
// `PayrollRunAdjustment.ManualLineItemsJson`.
//
// Distinct from `FixedAllowance`, which lives on the employee's profile and
// applies to every run. Structurally the two are the same shape, and that is
// deliberate: the generator merges manual rows into the fixed list and lets the
// calculator's one category-aware routing loop handle both. A manual deduction
// therefore respects `ReducesBase`, exemption ceilings and the six `SubjectTo*`
// flags exactly as a recurring one does — which is the whole reason for
// routing it through the catalogue rather than as a free-form amount.
public sealed record ManualLineItem
{
    // Derived from the category on save rather than taken from the client, so
    // the stored kind can never contradict the catalogue the calculator
    // actually dispatches on. Present for the UI's benefit only.
    public PayslipLineKind Kind { get; init; } = PayslipLineKind.ALLOWANCE;

    // A `PayrollAdjustmentCategories` code. Validated on save — an unrecognised
    // code is skipped silently at calculation time, so letting one through
    // would look like the admin's row simply vanished.
    public string Category { get; init; } = PayrollAdjustmentCategories.AllowanceStandard;

    // The admin's own label. Falls back to the category's label when blank.
    public string? Label { get; init; }

    public decimal Amount { get; init; }

    // Only meaningful on an `IsAdditionalRemuneration` category. See
    // `FixedAllowance.TreatAsRecurring` — same flag, same reasoning: an
    // allowance paid every month is not the one-off spike LHDN's additional
    // remuneration formula assumes, and taxing it as one over-withholds.
    public bool TreatAsRecurring { get; init; }
}
