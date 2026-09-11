namespace AltomateHR.Api.Modules.Payroll;

// The resident progressive tax bands and the individual rebate.
//
// ⚠️ Rates are LHDN's 2024 individual-resident schedule. The 2026 MTD spec
// confirms the FORMULA is unchanged — only the TP1/TP3 deduction items moved —
// so these bands stay current until LHDN shifts a threshold.
// Source: https://www.hasil.gov.my/en/employers/employer-payroll-data-specification/
public static class PcbTaxBands
{
    // Marginal rate applying up to and including UpperBound (annual chargeable
    // income, RM). The last band is open-ended.
    private readonly record struct Band(decimal UpperBound, decimal Rate);

    private static readonly IReadOnlyList<Band> ResidentBands2024 =
    [
        new(5000m, 0m),
        new(20000m, 0.01m),
        new(35000m, 0.03m),
        new(50000m, 0.06m),
        new(70000m, 0.11m),
        new(100000m, 0.19m),
        new(400000m, 0.25m),
        new(600000m, 0.26m),
        new(2000000m, 0.28m),
        new(decimal.MaxValue, 0.30m),
    ];

    // The annual rebate (LHDN MTD Spec 2026, Table 1 note) is allowed only when
    // annual chargeable income P is at or below this.
    private const decimal RebateThreshold = 35000m;
    private const decimal RebateIndividual = 400m;

    // Category 2 (married, spouse not working) claims the spouse rebate on top,
    // taking the total to RM 800.
    private const decimal RebateSpouse = 400m;

    // Annual tax on a chargeable income, net of rebate.
    //
    // LHDN bakes the rebate into Table 1's `B` column — the negative B values on
    // the 5,001–20,000 and 20,001–35,000 rows encode "marginal tax minus rebate"
    // so that `(P − M)R + B` yields post-rebate tax in one step. We do the same
    // arithmetic in two visible steps instead.
    //
    // `spouseClaimable` is the same gate as the RM 4,000 S relief: married AND
    // the spouse has no income.
    public static decimal ApplyResidentBands(
        decimal chargeableIncome, bool spouseClaimable = false)
    {
        if (chargeableIncome <= 0m) return 0m;

        var tax = 0m;
        var previousBound = 0m;

        foreach (var band in ResidentBands2024)
        {
            if (chargeableIncome <= band.UpperBound)
            {
                tax += (chargeableIncome - previousBound) * band.Rate;
                break;
            }

            tax += (band.UpperBound - previousBound) * band.Rate;
            previousBound = band.UpperBound;
        }

        // The threshold test floors the income so sub-ringgit drift out of the
        // LHDN K decomposition — which loses ~0.07 of EPF-cap utilisation —
        // can't push a boundary case (RM 4,000/month single lands at 35,000.07)
        // over the line and silently strip a RM 400 rebate. The tax arithmetic
        // above still uses the exact figure.
        if (Math.Floor(chargeableIncome) <= RebateThreshold)
        {
            tax -= RebateIndividual + (spouseClaimable ? RebateSpouse : 0m);
        }

        return Math.Max(0m, tax);
    }

    // The {M, R, B} triple for the band containing P, in LHDN's own symbols: M
    // is the bracket's lower bound, R its marginal rate, and B the cumulative
    // tax at M less any rebate — so `(P − M) × R + B` reproduces the annual tax.
    //
    // Only the LHDN-form breakdown needs this; the money itself comes from
    // ApplyResidentBands.
    public static (decimal M, decimal R, decimal B) FindBand(
        decimal chargeableIncome, bool spouseClaimable)
    {
        if (chargeableIncome <= 0m) return (0m, 0m, 0m);

        var previousBound = 0m;
        var cumulativeTax = 0m;

        foreach (var band in ResidentBands2024)
        {
            if (chargeableIncome <= band.UpperBound)
            {
                var b = cumulativeTax;
                if (chargeableIncome <= RebateThreshold)
                {
                    b -= RebateIndividual + (spouseClaimable ? RebateSpouse : 0m);
                }

                return (previousBound, band.Rate, b);
            }

            cumulativeTax += (band.UpperBound - previousBound) * band.Rate;
            previousBound = band.UpperBound;
        }

        // Unreachable — the last band is open-ended.
        return (previousBound, 0.30m, cumulativeTax);
    }
}
