using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// Pins LHDN Table 1. If one of these fails, either LHDN moved a threshold or the
// band list was edited wrongly — check the published schedule before touching an
// expected value.
public class PcbTaxBandsTests
{
    [Theory]
    // The 0% band. Nothing owed with or without the rebate.
    [InlineData(0, 0)]
    [InlineData(5000, 0)]
    // Marginal 150 − rebate 400 → 0.
    [InlineData(20000, 0)]
    // Marginal 348 − rebate 400 → 0. LHDN Table 1 worked value.
    [InlineData(26600, 0)]
    // Marginal 450 − rebate 400 → 50.
    [InlineData(30000, 50)]
    // The rebate still applies AT the threshold, not just below it.
    [InlineData(35000, 200)]
    // Above the threshold the rebate is gone: 150 + 450 + 900 = 1,500.
    [InlineData(50000, 1500)]
    public void Category1_SingleOrSpouseWorking(decimal chargeable, decimal expected)
    {
        Assert.Equal(expected, PcbTaxBands.ApplyResidentBands(chargeable, spouseClaimable: false));
    }

    // Category 2 doubles the rebate to RM 800 — but only under the threshold.
    [Fact]
    public void Category2_DoublesTheRebateBelowTheThreshold()
    {
        Assert.Equal(0m, PcbTaxBands.ApplyResidentBands(30000m, spouseClaimable: true));
    }

    [Fact]
    public void Category2_GetsNoRebateAboveTheThreshold()
    {
        Assert.Equal(
            PcbTaxBands.ApplyResidentBands(50000m, spouseClaimable: false),
            PcbTaxBands.ApplyResidentBands(50000m, spouseClaimable: true));
    }

    // The rebate can exceed the marginal tax; it must not turn into a refund.
    [Theory]
    [InlineData(-1000)]
    [InlineData(0)]
    [InlineData(6000)]
    [InlineData(15000)]
    public void TaxNeverGoesNegative(decimal chargeable)
    {
        Assert.True(PcbTaxBands.ApplyResidentBands(chargeable, false) >= 0m);
        Assert.True(PcbTaxBands.ApplyResidentBands(chargeable, true) >= 0m);
    }

    // Each band is marginal, not a cliff — crossing a boundary must not jump the
    // whole income to the higher rate.
    [Fact]
    public void BandsAreMarginal()
    {
        var justBelow = PcbTaxBands.ApplyResidentBands(70000m, false);
        var justAbove = PcbTaxBands.ApplyResidentBands(70100m, false);

        // 100 more income at 19% = RM 19 more tax, not a step change.
        Assert.Equal(19m, justAbove - justBelow);
    }

    // The top band is open-ended.
    [Fact]
    public void AboveTwoMillion_ChargesThirtyPercentOnTheExcess()
    {
        var atCap = PcbTaxBands.ApplyResidentBands(2_000_000m, false);
        var above = PcbTaxBands.ApplyResidentBands(2_100_000m, false);

        Assert.Equal(30_000m, above - atCap);
    }

    // ─── FindBand: the {M, R, B} triple for the LHDN form ───────────────

    // `(P − M) × R + B` must reproduce ApplyResidentBands exactly, since the
    // rendered form and the deducted money come from the two different paths.
    [Theory]
    [InlineData(20000)]
    [InlineData(26600)]
    [InlineData(35000)]
    [InlineData(47000)]
    [InlineData(83000)]
    [InlineData(550000)]
    public void FindBand_ReproducesTheSameTax(decimal chargeable)
    {
        foreach (var spouseClaimable in new[] { false, true })
        {
            var (m, r, b) = PcbTaxBands.FindBand(chargeable, spouseClaimable);
            var viaFormula = Math.Max(0m, (chargeable - m) * r + b);

            Assert.Equal(
                PcbTaxBands.ApplyResidentBands(chargeable, spouseClaimable),
                viaFormula);
        }
    }

    // The LHDN worked example's own band: P = 47,000 sits in the 35k–50k band at
    // 6%, with B = 600 cumulative tax at 35,000.
    [Fact]
    public void FindBand_MatchesTheLhdnWorkedExample()
    {
        var (m, r, b) = PcbTaxBands.FindBand(47000m, spouseClaimable: false);

        Assert.Equal(35000m, m);
        Assert.Equal(0.06m, r);
        Assert.Equal(600m, b);
    }
}
