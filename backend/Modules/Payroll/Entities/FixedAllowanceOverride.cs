namespace AltomateHR.Api.Modules.Payroll.Entities;

// A per-run override of ONE row in the employee's profile-level
// `FixedAllowances` array. Persisted as a JSON object in
// `PayrollRunAdjustment.FixedAllowanceOverridesJson`, keyed by the row's array
// INDEX as a string:
//
//     { "0": { "amount": 250 }, "2": { "skip": true } }
//
// Sparse — an index with no entry uses the profile's own amount.
//
// ⚠ The key is positional, not an identity. Reordering (or deleting from the
// middle of) an employee's fixed allowances re-points every override on every
// draft run that referenced them. The shape is the reference app's and is kept
// for parity; `PayrollRunAdjustments.ApplyOverrides` is written so an index
// past the end of the array is ignored rather than throwing.
public sealed record FixedAllowanceOverride
{
    // Replaces the profile's amount for this run only. Null = keep it.
    //
    // The original row's CATEGORY is always preserved, so the six `SubjectTo*`
    // flags still follow through. Overriding the amount while losing the
    // category would silently move, say, a travel allowance into the EPF base.
    public decimal? Amount { get; init; }

    // Zero this row out for this run only.
    public bool Skip { get; init; }
}
