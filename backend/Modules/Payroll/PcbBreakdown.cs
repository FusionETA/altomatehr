namespace AltomateHR.Api.Modules.Payroll;

// The LHDN MTD formula, decomposed into the symbols the form itself uses.
//
// This is what `Payslip.PcbCalculationJson` stores and what the Detailed
// Calculations PDF renders. An employee — or an LHDN officer — can take these
// numbers, do the arithmetic by hand, and arrive at the figure that was
// actually deducted.
//
// ⚠ That last sentence is the whole point, and it is why `PcbCalculator`
// produces this breakdown and the deducted money in ONE pass, deriving
// `PcbCalculator.Result` from these fields rather than computing the money
// separately.
//
// The reference implementation has two routines — `calcPcb` for the money and
// `calcPcbBreakdown` for the form — with a comment asking whoever edits one to
// remember the other. They have already drifted: the form subtracts a
// 2dp-ROUNDED CS while the money subtracts the unrounded value, so the PDF can
// print a PCB(C) a sen away from the sum that left the employee's pay. Two
// implementations of one formula is the defect; this port has one.
//
// Persisted as a flat record with a discriminator rather than a polymorphic
// hierarchy: it is an audit snapshot that has to deserialise years from now,
// and a plain shape survives that better than type resolution does.
public sealed record PcbBreakdown
{
    public required PcbFormula Formula { get; init; }

    // ---- Both formulas ----

    // This month's normal taxable remuneration, and the one-off amount taxed
    // through the additional-remuneration path. `Yt` on the form.
    public decimal NormalTaxable { get; init; }
    public decimal AdditionalTaxable { get; init; }

    // ---- Non-resident only ----

    // The flat withholding rate — 0.30. No reliefs, no annualisation.
    public decimal Rate { get; init; }

    // ---- Resident: income and EPF, split the way the form splits them ----

    // Y  — remuneration for the year BEFORE this month.
    // K  — EPF relief already used by those months.
    // Y1 — this month's normal remuneration.
    // K1 — this month's EPF relief, shown in whole ringgit (the form's
    //      convention; K2 absorbs the sen inside the same RM 4,000 cap).
    // Y2 — the projection: Y1 repeated for each remaining month.
    // K2 — the per-month EPF relief in that projection.
    // N  — months remaining AFTER this one. The divisor is N + 1.
    public decimal Y { get; init; }
    public decimal K { get; init; }
    public decimal Y1 { get; init; }
    public decimal K1 { get; init; }
    public decimal Y2 { get; init; }
    public decimal K2 { get; init; }
    public int N { get; init; }

    // ---- Resident: reliefs ----

    // D  — individual.            Du — disabled individual.
    // S  — spouse.                Su — disabled spouse.
    // QC — total child relief.
    //
    // Q and C exist because the form shows child relief as "Q × C". Real
    // children do not share one rate (RM 2,000 / 8,000 / 16,000), so Q is the
    // standard per-child amount and C is QC ÷ Q — which reads correctly for the
    // common case and can be fractional otherwise. QC is always the true sum,
    // and QC is what the arithmetic uses.
    public decimal D { get; init; }
    public decimal Du { get; init; }
    public decimal S { get; init; }
    public decimal Su { get; init; }
    public decimal Q { get; init; }
    public decimal C { get; init; }
    public decimal QC { get; init; }

    // ΣLP — allowable deductions accumulated before this month.
    // LP1 — this month's. Both carry the PERKESO relief and the TP1 items.
    public decimal SumLp { get; init; }
    public decimal Lp1 { get; init; }

    // ---- Resident: the annual figure and its band ----

    // P — annual chargeable income:
    //     [(Y − K) + (Y1 − K1) + (Y2 − K2 × N)] − (D + S + Du + Su + QC + ΣLP + LP1)
    public decimal P { get; init; }

    // The band containing P: M is its lower bound, R the marginal rate, B the
    // cumulative tax at M less any rebate. (P − M) × R + B is the annual tax.
    public decimal M { get; init; }
    public decimal R { get; init; }
    public decimal B { get; init; }

    // Z — zakat paid earlier this year. X — PCB already deducted this year.
    public decimal Z { get; init; }
    public decimal X { get; init; }

    public decimal YearlyTax { get; init; }

    // (YearlyTax − Z − X) ÷ (N + 1), before the RM 10 threshold and rounding.
    public decimal CurrentMonthPcb { get; init; }

    // Null when no additional remuneration was paid this month.
    public PcbArBreakdown? Ar { get; init; }

    // ---- What was actually deducted ----
    // PCB(A), PCB(C) and the rounded sum. These are the same values
    // `PcbCalculator.Result` carries — by construction, not by agreement.
    public decimal PcbNormal { get; init; }
    public decimal PcbAdditional { get; init; }
    public decimal PcbTotal { get; init; }
}

public enum PcbFormula
{
    // Flat 30% withholding, no reliefs.
    NonResident,

    // Annualise, relieve, band, spread over the months remaining.
    Resident,
}

// LHDN MTD Specification 2026 Section E, the five steps in order.
public sealed record PcbArBreakdown
{
    // Step 3 inputs.

    // Yt — the additional remuneration itself.
    public decimal Yt { get; init; }

    // Kt — the EPF the employee actually paid on Yt. Shown on the form.
    public decimal Kt { get; init; }

    // How much of Kt earns relief: whatever is left of the RM 4,000 annual
    // budget once the normal projection (K + K1 + K2 × N) has taken its share.
    // Often near zero, and that is correct — the contribution is still made,
    // it just buys no further relief. The chargeable figure below uses THIS,
    // not Kt, so a reader reconciling the form is not misled by the difference.
    public decimal KtEffective { get; init; }

    // P with the additional remuneration layered on, and its band. M2/R2/B2
    // differ from M/R/B only when the AR pushes P past a bracket boundary.
    public decimal ChargeableWithAr { get; init; }
    public decimal M2 { get; init; }
    public decimal R2 { get; init; }
    public decimal B2 { get; init; }

    // Step 3 — CS, the yearly tax including the AR: (P₂ − M₂) × R₂ + B₂.
    public decimal Cs { get; init; }

    // Step 2 — PCB(B), the projected annual NORMAL deduction:
    //          X + trunc2(CurrentMonthPcb) × (N + 1).
    //
    // Note the TRUNCATED monthly figure, not the thresholded PCB(A). The spec
    // writes "PCB(A) × (n+1)" but means the truncated value; using the
    // thresholded one would drop a whole month's projection whenever the
    // monthly deduction sits under RM 10.
    public decimal PcbB { get; init; }

    // Step 4 — CS − PCB(B) − Z, before the RM 10 threshold and truncation.
    public decimal PcbCBeforeRounding { get; init; }

    // Step 4, applied. Step 5's net figure is the breakdown's PcbTotal.
    public decimal PcbC { get; init; }
}
