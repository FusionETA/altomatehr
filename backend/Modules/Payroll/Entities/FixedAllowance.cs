namespace AltomateHR.Api.Modules.Payroll.Entities;

// One recurring adjustment that applies to an employee on every run. Persisted
// as a JSON array in `EmployeeProfile.FixedAllowancesJson` — the Employees
// module owns the column, payroll owns the shape.
//
// The `Category` string is a `PayrollAdjustmentCategories` code. It is a string
// rather than an enum because it is stored in free-form JSON written by an
// older system: an unrecognised code has to be skippable, not a deserialisation
// failure that takes the whole run down.
public sealed record FixedAllowance
{
    public string Category { get; init; } = PayrollAdjustmentCategories.AllowanceStandard;

    // The admin's own label for this row. Falls back to the category's label.
    public string? Name { get; init; }

    public decimal Amount { get; init; }

    // Only meaningful on an `IsAdditionalRemuneration` category. Ticking it
    // routes the amount through the smoothed monthly PCB path instead of LHDN's
    // one-shot additional-remuneration formula — an annual bonus paid in twelve
    // equal parts really is recurring, and taxing it as a spike over-withholds.
    //
    // It has never controlled EPF: EPF Act 1991 s.2 reads "wages" broadly, so a
    // bonus always joins the regular wage that picks the KWSP Third Schedule
    // tier. Letting one flag drive both forced admins to choose between right
    // EPF and right PCB on the same line.
    public bool TreatAsRecurring { get; init; }
}
