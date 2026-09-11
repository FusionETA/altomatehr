using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

public class EpfCalculatorTests
{
    private static EpfCalculator.Input Profile(
        decimal wage = 8000m,
        decimal employeeRate = 11m,
        bool contributeToEpf = true,
        bool isMalaysianCitizen = true,
        bool hasPr = false,
        bool epfMemberBefore1998 = false,
        int ageAtPeriodEnd = 40,
        decimal additionalRemuneration = 0m,
        decimal? rateDeterminingWage = null,
        decimal employeeVoluntary = 0m,
        decimal employerVoluntary = 0m) => new()
        {
            Wage = wage,
            EmployeeRate = employeeRate,
            ContributeToEpf = contributeToEpf,
            IsMalaysianCitizen = isMalaysianCitizen,
            HasPr = hasPr,
            EpfMemberBefore1998 = epfMemberBefore1998,
            AgeAtPeriodEnd = ageAtPeriodEnd,
            AdditionalRemuneration = additionalRemuneration,
            RateDeterminingWage = rateDeterminingWage,
            EmployeeVoluntary = employeeVoluntary,
            EmployerVoluntary = employerVoluntary,
        };

    // ─── Branch resolver ────────────────────────────────────────────────

    [Fact]
    public void Under60Malaysian_IsPartA()
    {
        Assert.Equal(
            EpfBranch.MALAYSIAN_UNDER_60,
            EpfCalculator.PickBranch(true, 8000m, true, false, false, 59));
    }

    // Regression: the profile UI auto-locks hasPr=true for Malaysian citizens, so
    // a branch test of `isMalaysianCitizen && !hasPr` sent them to Part C and
    // over-collected 5.5% employee EPF from people entitled to Part E's 0%.
    // Citizenship alone decides Part E at 60+.
    [Theory]
    [InlineData(60, false)]
    [InlineData(62, true)]
    [InlineData(75, true)]
    public void MalaysianCitizen60Plus_IsPartE_WhateverThePrFlag(int age, bool hasPr)
    {
        Assert.Equal(
            EpfBranch.MALAYSIAN_CITIZEN_60_PLUS,
            EpfCalculator.PickBranch(true, 8000m, isMalaysianCitizen: true, hasPr, false, age));
    }

    [Fact]
    public void NonMalaysianPr60Plus_IsPartC()
    {
        Assert.Equal(
            EpfBranch.PR_OR_PRE1998_60_PLUS,
            EpfCalculator.PickBranch(true, 8000m, isMalaysianCitizen: false, hasPr: true, false, 65));
    }

    [Fact]
    public void NonMalaysianPre1998Member60Plus_IsPartC()
    {
        Assert.Equal(
            EpfBranch.PR_OR_PRE1998_60_PLUS,
            EpfCalculator.PickBranch(
                true, 8000m, isMalaysianCitizen: false, hasPr: false,
                epfMemberBefore1998: true, ageAtPeriodEnd: 62));
    }

    [Theory]
    [InlineData(25)]
    [InlineData(35)]
    [InlineData(70)]
    public void Post1998NonMalaysian_IsPartF_AtAnyAge(int age)
    {
        Assert.Equal(
            EpfBranch.POST_1998_NON_MALAYSIAN,
            EpfCalculator.PickBranch(
                true, 8000m, isMalaysianCitizen: false, hasPr: false,
                epfMemberBefore1998: false, ageAtPeriodEnd: age));
    }

    [Fact]
    public void OptedOut_ShortCircuitsEverything()
    {
        Assert.Equal(
            EpfBranch.OPTED_OUT,
            EpfCalculator.PickBranch(false, 8000m, true, false, false, 45));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(5)]
    [InlineData(0)]
    public void DeMinimisWage_ContributesNothing(decimal wage)
    {
        Assert.Equal(
            EpfBranch.DE_MINIMIS,
            EpfCalculator.PickBranch(true, wage, true, false, false, 40));
    }

    [Theory]
    [InlineData(false, 8000)]   // opted out
    [InlineData(true, 10)]      // de minimis
    public void NonContributingBranches_ProduceZeroBothSides(bool contributes, decimal wage)
    {
        var result = EpfCalculator.Calculate(Profile(wage: wage, contributeToEpf: contributes));

        Assert.Equal(0m, result.Employee);
        Assert.Equal(0m, result.Employer);
    }

    // ─── Money, by branch ───────────────────────────────────────────────

    // Part A below the cliff: RM 20 bands, employer 13%, employee 11%.
    [Fact]
    public void PartA_BelowTheCliff_UsesTheBandTable()
    {
        var result = EpfCalculator.Calculate(Profile(wage: 4100m));

        Assert.Equal(EpfBranch.MALAYSIAN_UNDER_60, result.Branch);
        Assert.Equal(533m, result.Employer);   // ceil(13% × 4,100)
        Assert.Equal(451m, result.Employee);   // ceil(11% × 4,100)
    }

    // Part A above the cliff: RM 100 bands, employer drops to 12%.
    // The RM 6 gap this pins used to come from computing the employer side as an
    // exact percentage while the employee side used the table.
    [Fact]
    public void PartA_AboveTheCliff_BandsBothSides()
    {
        var result = EpfCalculator.Calculate(Profile(wage: 8250m));

        Assert.Equal(996m, result.Employer);   // ceil(12% × band-up 8,300)
        Assert.Equal(913m, result.Employee);   // ceil(11% × band-up 8,300)
    }

    [Fact]
    public void PartE_MalaysianCitizen60Plus_TakesNothingFromTheEmployee()
    {
        var result = EpfCalculator.Calculate(Profile(wage: 5500m, ageAtPeriodEnd: 62));

        Assert.Equal(EpfBranch.MALAYSIAN_CITIZEN_60_PLUS, result.Branch);
        Assert.Equal(220m, result.Employer);   // 4% flat
        Assert.Equal(0m, result.Employee);
    }

    [Fact]
    public void PartF_Post1998NonMalaysian_IsFlatTwoPercentEachSide()
    {
        var result = EpfCalculator.Calculate(
            Profile(wage: 8000m, isMalaysianCitizen: false, ageAtPeriodEnd: 35));

        Assert.Equal(EpfBranch.POST_1998_NON_MALAYSIAN, result.Branch);
        Assert.Equal(160m, result.Employer);
        Assert.Equal(160m, result.Employee);
    }

    // The admin's declared rate can only raise the Part A employee share, never
    // lower it — the COVID-era 9% election has ended.
    [Theory]
    [InlineData(9)]
    [InlineData(0)]
    [InlineData(11)]
    public void PartA_ClampsTheDeclaredEmployeeRateUpToEleven(decimal declaredRate)
    {
        var result = EpfCalculator.Calculate(Profile(wage: 4100m, employeeRate: declaredRate));

        Assert.Equal(451m, result.Employee);   // 11% of 4,100, not 9% or 0%
    }

    [Fact]
    public void PartA_HonoursADeclaredRateAboveEleven()
    {
        var result = EpfCalculator.Calculate(Profile(wage: 4100m, employeeRate: 15m));

        Assert.Equal(615m, result.Employee);   // ceil(15% × 4,100)
    }

    // Non-Part-A branches ignore the declared rate entirely — the statute fixes it.
    [Fact]
    public void PartE_IgnoresTheDeclaredEmployeeRate()
    {
        var result = EpfCalculator.Calculate(
            Profile(wage: 5500m, employeeRate: 11m, ageAtPeriodEnd: 65));

        Assert.Equal(0m, result.Employee);
    }

    // ─── Voluntary contributions ────────────────────────────────────────

    // On the band-table path, voluntary is a separate ceil on top of the gazetted
    // mandatory amount — the Schedule has no concept of voluntary.
    [Fact]
    public void BandTablePath_AddsVoluntaryAsASeparateCeiling()
    {
        var result = EpfCalculator.Calculate(
            Profile(wage: 4100m, employeeVoluntary: 5m, employerVoluntary: 2m));

        Assert.Equal(451m + 205m, result.Employee);   // band 451 + ceil(5% × 4,100)
        Assert.Equal(533m + 82m, result.Employer);    // band 533 + ceil(2% × 4,100)
    }

    // Off table, KWSP rounds up ONCE PER SIDE. Ceiling mandatory and voluntary
    // separately gave 293 + 1,319 = 1,612 where the correct answer is
    // ceil(11% × 14,645) = 1,611.
    [Fact]
    public void OffTablePath_CeilsTheCombinedRateOncePerSide()
    {
        var result = EpfCalculator.Calculate(Profile(
            wage: 14645m,
            isMalaysianCitizen: false,   // Part F, flat → off table
            employeeVoluntary: 9m,
            employerVoluntary: 9m));

        Assert.Equal(1611m, result.Employee);   // ceil(11% × 14,645), not 1,612
        Assert.Equal(1611m, result.Employer);
    }

    // ─── The RM 5,000 cliff and additional remuneration ─────────────────

    // A one-off bonus must not move a sub-RM-5,000 employee onto the 12% tier.
    // Regular 4,100 + bonus 1,218 → employer 692, not 702.
    [Fact]
    public void ABonusMonth_KeepsTheEmployerRateAtTheContractualTier()
    {
        var result = EpfCalculator.Calculate(Profile(
            wage: 5318m,                    // 4,100 regular + 1,218 bonus
            rateDeterminingWage: 4100m));   // the cliff reads the regular portion

        Assert.Equal(692m, result.Employer);   // ceil(13% × 5,318)
    }

    // Without the override the same wage crosses the cliff and pays 12%.
    [Fact]
    public void WithoutTheOverride_TheCliffReadsTheWholeWage()
    {
        var result = EpfCalculator.Calculate(Profile(wage: 5318m));

        Assert.Equal(EpfBranch.MALAYSIAN_UNDER_60, result.Branch);
        Assert.Equal(648m, result.Employer);   // ceil(12% × band-up 5,400)
    }

    // Above RM 20,000 every branch leaves the table.
    [Fact]
    public void AboveTheBandTableCeiling_UsesExactPercentages()
    {
        var result = EpfCalculator.Calculate(Profile(wage: 25750m));

        Assert.Equal(3090m, result.Employer);   // ceil(12% × 25,750)
        Assert.Equal(2833m, result.Employee);   // ceil(11% × 25,750)
    }
}
