using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// Snapshots the gazetted Third Schedule at a handful of wage points. A failure
// here means either the schedule genuinely changed (download the latest from
// KWSP / PERKESO) or the table was edited wrongly.
//
// Do NOT "fix" a failure by updating the expected value — cross-check the source
// PDF first. These numbers are law, not preference.
public class StatutoryTablesTests
{
    // ─── SOCSO Act 4 ────────────────────────────────────────────────────

    [Fact]
    public void SocsoTable_Has65Rows()
    {
        Assert.Equal(65, StatutoryTables.SocsoTable.Count);
    }

    [Fact]
    public void SocsoTable_IsSortedStrictlyIncreasing()
    {
        for (var i = 1; i < StatutoryTables.SocsoTable.Count; i++)
        {
            Assert.True(
                StatutoryTables.SocsoTable[i].UpTo > StatutoryTables.SocsoTable[i - 1].UpTo,
                $"SOCSO row {i} is not above row {i - 1}");
        }
    }

    [Fact]
    public void SocsoTable_OnlyTheLastRowIsTheCeiling()
    {
        var ceilingRows = StatutoryTables.SocsoTable.Count(r => r.UpTo == decimal.MaxValue);

        Assert.Equal(1, ceilingRows);
        Assert.Equal(decimal.MaxValue, StatutoryTables.SocsoTable[^1].UpTo);
    }

    [Fact]
    public void Socso_Row1_UpToRm30()
    {
        Assert.Equal((0.4m, 0.1m), StatutoryTables.LookupSocso(25m, category2: false));
        Assert.Equal((0.3m, 0m), StatutoryTables.LookupSocso(25m, category2: true));
    }

    [Fact]
    public void Socso_Row65_AboveTheCeiling()
    {
        Assert.Equal((104.15m, 29.75m), StatutoryTables.LookupSocso(10_000m, category2: false));
        Assert.Equal((74.4m, 0m), StatutoryTables.LookupSocso(6_000.01m, category2: true));
    }

    [Fact]
    public void Socso_AtTheCeilingExactly_MatchesRow64()
    {
        Assert.Equal((104.15m, 29.75m), StatutoryTables.LookupSocso(6_000m, category2: false));
    }

    [Fact]
    public void Socso_ZeroOrNegativeWage_ContributesNothing()
    {
        Assert.Equal((0m, 0m), StatutoryTables.LookupSocso(0m, category2: false));
        Assert.Equal((0m, 0m), StatutoryTables.LookupSocso(-100m, category2: true));
    }

    // ─── EIS Act 800 ────────────────────────────────────────────────────

    [Fact]
    public void EisTable_Has65Rows()
    {
        Assert.Equal(65, StatutoryTables.EisTable.Count);
    }

    [Fact]
    public void EisTable_IsSortedStrictlyIncreasing()
    {
        for (var i = 1; i < StatutoryTables.EisTable.Count; i++)
        {
            Assert.True(
                StatutoryTables.EisTable[i].UpTo > StatutoryTables.EisTable[i - 1].UpTo,
                $"EIS row {i} is not above row {i - 1}");
        }
    }

    [Fact]
    public void EisTable_OnlyTheLastRowIsTheCeiling()
    {
        var ceilingRows = StatutoryTables.EisTable.Count(r => r.UpTo == decimal.MaxValue);

        Assert.Equal(1, ceilingRows);
        Assert.Equal(decimal.MaxValue, StatutoryTables.EisTable[^1].UpTo);
    }

    [Fact]
    public void Eis_Row1_FiveSenEachSide()
    {
        Assert.Equal((0.05m, 0.05m), StatutoryTables.LookupEis(25m));
    }

    [Fact]
    public void Eis_Row65_CapsAtRm11Point90EachSide()
    {
        Assert.Equal((11.9m, 11.9m), StatutoryTables.LookupEis(10_000m));
    }

    [Fact]
    public void Eis_ZeroWage_ContributesNothing()
    {
        Assert.Equal((0m, 0m), StatutoryTables.LookupEis(0m));
    }

    // ─── KWSP Third Schedule rule (Parts A, C, E, F) ────────────────────

    // Part A — Malaysian / PR / pre-1998 under 60: employer 13→12%, employee 11%.
    [Theory]
    // Wage exactly on a RM 20 band edge.
    [InlineData(100, 13, 11)]
    // Mid-band 100.01–120 rounds up to the RM 120 bound.
    [InlineData(120, 16, 14)]
    // Last RM 20 band before the cliff.
    [InlineData(5000, 650, 550)]
    // First RM 100 band; employer rate steps down to 12%. Band upper = 5,100.
    [InlineData(5001, 612, 561)]
    // Last tabulated row.
    [InlineData(20000, 2400, 2200)]
    // Above the table: exact percentage, each side rounded up to the next ringgit.
    // employer = ceil(12% × 25,750) = 3,090; employee = ceil(11% × 25,750) = 2,833.
    [InlineData(25750, 3090, 2833)]
    public void EpfPartA_FollowsTheBandRule(decimal wage, decimal employer, decimal employee)
    {
        var result = StatutoryTables.LookupEpfBand(
            wage, employerRateLow: 13m, employerRateHigh: 12m, employeeRate: 11m);

        Assert.Equal((employer, employee), result);
    }

    [Theory]
    [InlineData(9.99)]
    [InlineData(10)]
    [InlineData(0)]
    public void Epf_AtOrBelowDeMinimis_ContributesNothing(decimal wage)
    {
        var result = StatutoryTables.LookupEpfBand(
            wage, employerRateLow: 13m, employerRateHigh: 12m, employeeRate: 11m);

        Assert.Equal((0m, 0m), result);
    }

    // Part C — PR / pre-1998 non-Malaysian at 60+: employer 6.5→6%, employee 5.5%.
    // Band upper = 100: employer = ceil(6.5) = 7; employee = ceil(5.5) = 6.
    [Fact]
    public void EpfPartC_RoundsEachSideUpIndependently()
    {
        var result = StatutoryTables.LookupEpfBand(
            100m, employerRateLow: 6.5m, employerRateHigh: 6m, employeeRate: 5.5m);

        Assert.Equal((7m, 6m), result);
    }

    // Part E — Malaysian citizen 60+: employer 4% flat, employee nil.
    [Fact]
    public void EpfPartE_FlatRate_NoEmployeeShare()
    {
        var result = StatutoryTables.LookupEpfBand(
            5500m, employerRateLow: 4m, employerRateHigh: 4m, employeeRate: 0m);

        Assert.Equal((220m, 0m), result);
    }

    // Part F — post-1998 non-Malaysian: 2% / 2% flat at every wage.
    [Fact]
    public void EpfPartF_FlatRate_BothSides()
    {
        var result = StatutoryTables.LookupEpfBand(
            8000m, employerRateLow: 2m, employerRateHigh: 2m, employeeRate: 2m);

        Assert.Equal((160m, 160m), result);
    }

    // The regression that motivated detecting flat-rate branches by the absence
    // of a cliff: banding RM 13,946 up to RM 14,000 would charge RM 280 where the
    // off-table rule gives ceil(2% × 13,946) = RM 279.
    [Fact]
    public void EpfPartF_IsNotBanded()
    {
        var result = StatutoryTables.LookupEpfBand(
            13946m, employerRateLow: 2m, employerRateHigh: 2m, employeeRate: 2m);

        Assert.Equal((279m, 279m), result);
    }

    // The cliff normally reads the full wage, so a bonus month tips the employee
    // into the 12% tier. `rateDeterminingWage` lets a caller keep them at 13% on
    // their contractual salary while still contributing on the whole amount.
    [Fact]
    public void Epf_RateDeterminingWage_HoldsTheCliffAtTheContractualSalary()
    {
        // RM 4,500 salary + RM 1,000 bonus. Band upper = ceil(5500/20)×20 = 5,500.
        var heldLow = StatutoryTables.LookupEpfBand(
            5500m, employerRateLow: 13m, employerRateHigh: 12m, employeeRate: 11m,
            rateDeterminingWage: 4500m);

        Assert.Equal((715m, 605m), heldLow);   // 13% and 11% of 5,500

        // Without the override the same wage crosses the cliff: RM 100 bands, 12%.
        var crossed = StatutoryTables.LookupEpfBand(
            5500m, employerRateLow: 13m, employerRateHigh: 12m, employeeRate: 11m);

        Assert.Equal((660m, 605m), crossed);   // 12% and 11% of 5,500
    }

    // ─── SKBBK / Skim LINDUNG 24 Jam ────────────────────────────────────

    // Gazette-verbatim amounts. Row 5 is the one that proves these are read, not
    // computed: 0.75% × 140 would be RM 1.05, but the gazette says RM 0.90.
    [Theory]
    [InlineData(0, 0.20)]
    [InlineData(4, 0.90)]
    [InlineData(17, 10.15)]
    [InlineData(63, 44.65)]
    [InlineData(64, 44.65)]
    public void SkbbkAmounts_MatchTheGazette(int rowIndex, decimal expected)
    {
        Assert.Equal(expected, StatutoryTables.SocsoTable[rowIndex].Skbbk);
    }

    [Theory]
    [InlineData(2026, 5)]
    [InlineData(2025, 12)]
    public void Skbbk_BeforeJune2026_IsZero(int year, int month)
    {
        Assert.Equal(0m, StatutoryTables.LookupSkbbk(3000m, year, month));
    }

    [Fact]
    public void Skbbk_FromJune2026_UsesPhase1()
    {
        // Wage 3,000 lands on row 34 (UpTo 3,000).
        Assert.Equal(22.15m, StatutoryTables.LookupSkbbk(3000m, 2026, 6));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    [InlineData(10)]
    public void Skbbk_AtOrBelowDeMinimisWage_IsZero(decimal wage)
    {
        Assert.Equal(0m, StatutoryTables.LookupSkbbk(wage, 2026, 6));
    }

    [Fact]
    public void Skbbk_AboveTheCeiling_CapsAtRm44Point65()
    {
        Assert.Equal(44.65m, StatutoryTables.LookupSkbbk(10_000m, 2026, 6));
    }

    [Fact]
    public void Skbbk_WageExactly5000_LandsOnBand54()
    {
        Assert.Equal(
            StatutoryTables.SocsoTable[53].Skbbk,
            StatutoryTables.LookupSkbbk(5000m, 2026, 6));
    }

    [Theory]
    [InlineData(2026, 5)]
    [InlineData(2020, 1)]
    public void SkbbkPhase_IsNullBeforeAnyPhaseStarts(int year, int month)
    {
        Assert.Null(StatutoryTables.GetSkbbkPhaseForPeriod(year, month));
    }

    [Fact]
    public void SkbbkPhase_Phase1StartsJune2026()
    {
        var phase = StatutoryTables.GetSkbbkPhaseForPeriod(2026, 6);

        Assert.NotNull(phase);
        Assert.Equal(0.75m, phase.EmployeeRatePct);
        Assert.Equal(2026, phase.StartYear);
        Assert.Equal(6, phase.StartMonth);
    }

    // Phase 1 stays active until PERKESO gazettes phase 2 and a row is added.
    [Theory]
    [InlineData(2027, 1)]
    [InlineData(2099, 12)]
    public void SkbbkPhase_Phase1StaysActiveForFuturePeriods(int year, int month)
    {
        Assert.Equal(0.75m, StatutoryTables.GetSkbbkPhaseForPeriod(year, month)?.EmployeeRatePct);
    }
}
