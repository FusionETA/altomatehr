using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

public class PcbReliefsTests
{
    private static ChildRelief Child(
        ChildStudyingLevel studying = ChildStudyingLevel.UNDER_18,
        ChildAbilityStatus ability = ChildAbilityStatus.NORMAL,
        ChildPcbDeductionLevel claim = ChildPcbDeductionLevel.FULL) => new()
        {
            CurrentlyStudying = studying,
            AbilityStatus = ability,
            PcbDeduction = claim,
        };

    // ─── Per-child relief (LHDN Public Ruling 5/2019 §7.3) ──────────────

    [Theory]
    [InlineData(ChildStudyingLevel.UNDER_18, 2000)]
    [InlineData(ChildStudyingLevel.PRE_UNIVERSITY, 2000)]
    // Diploma+ in Malaysia and degree+ abroad both carry RM 8,000; they are
    // separate values only so Form EA can report the cohorts apart.
    [InlineData(ChildStudyingLevel.DIPLOMA_MALAYSIA, 8000)]
    [InlineData(ChildStudyingLevel.DEGREE_ABROAD, 8000)]
    public void StudyingLevel_SetsTheAmount(ChildStudyingLevel level, decimal expected)
    {
        Assert.Equal(expected, PcbReliefs.ForChild(Child(level)));
    }

    [Fact]
    public void ADisabledChild_Claims8000()
    {
        Assert.Equal(8000m, PcbReliefs.ForChild(
            Child(ability: ChildAbilityStatus.DISABLED)));
    }

    [Fact]
    public void ADisabledChildInHigherEducation_Claims16000()
    {
        Assert.Equal(16000m, PcbReliefs.ForChild(Child(
            ChildStudyingLevel.DIPLOMA_MALAYSIA, ChildAbilityStatus.DISABLED)));
    }

    // HALF is the 50/50 split when both parents claim the same child.
    [Theory]
    [InlineData(ChildStudyingLevel.UNDER_18, 1000)]
    [InlineData(ChildStudyingLevel.DIPLOMA_MALAYSIA, 4000)]
    public void AHalfClaim_HalvesTheAmount(ChildStudyingLevel level, decimal expected)
    {
        Assert.Equal(expected, PcbReliefs.ForChild(
            Child(level, claim: ChildPcbDeductionLevel.HALF)));
    }

    // NONE wins over everything — an unclaimed child contributes nothing even
    // when they would otherwise qualify for the largest amount.
    [Fact]
    public void AnUnclaimedChild_ContributesNothing()
    {
        Assert.Equal(0m, PcbReliefs.ForChild(Child(
            ChildStudyingLevel.DIPLOMA_MALAYSIA,
            ChildAbilityStatus.DISABLED,
            ChildPcbDeductionLevel.NONE)));
    }

    // ─── Personal and family reliefs ────────────────────────────────────

    [Fact]
    public void EveryResident_GetsTheIndividualRelief()
    {
        var reliefs = PcbReliefs.Itemise(false, spouseWorking: true, false, []);

        Assert.Equal(9000m, reliefs.Individual);
        Assert.Equal(9000m, reliefs.Total);
    }

    [Fact]
    public void AnOkuEmployee_AddsSevenThousand()
    {
        var reliefs = PcbReliefs.Itemise(isOku: true, spouseWorking: true, false, []);

        Assert.Equal(7000m, reliefs.DisabledIndividual);
        Assert.Equal(16000m, reliefs.Total);
    }

    // Only an explicit "spouse does not work" opens the S relief.
    [Theory]
    [InlineData(false, 4000)]
    [InlineData(true, 0)]
    [InlineData(null, 0)]
    public void SpouseRelief_NeedsADefiniteNonWorkingSpouse(
        bool? spouseWorking, decimal expected)
    {
        Assert.Equal(expected, PcbReliefs.Itemise(false, spouseWorking, false, []).Spouse);
    }

    [Fact]
    public void ADisabledNonWorkingSpouse_AddsSixThousandOnTop()
    {
        var reliefs = PcbReliefs.Itemise(false, spouseWorking: false, spouseDisabled: true, []);

        Assert.Equal(4000m, reliefs.Spouse);
        Assert.Equal(6000m, reliefs.DisabledSpouse);
        Assert.Equal(19000m, reliefs.Total);
    }

    // SU rides on S: a disabled spouse who works claims neither.
    [Fact]
    public void ADisabledWorkingSpouse_ClaimsNothing()
    {
        var reliefs = PcbReliefs.Itemise(false, spouseWorking: true, spouseDisabled: true, []);

        Assert.Equal(0m, reliefs.Spouse);
        Assert.Equal(0m, reliefs.DisabledSpouse);
    }

    // The LHDN worked example's profile: Cat 3 with three children under 18.
    [Fact]
    public void TheLhdnWorkedExampleProfile_Totals15000()
    {
        var reliefs = PcbReliefs.Itemise(
            false, spouseWorking: true, false, [Child(), Child(), Child()]);

        Assert.Equal(6000m, reliefs.Children);
        Assert.Equal(15000m, reliefs.Total);
    }

    // The itemised view must reconcile with its own total, and preserve the
    // declared order so the UI can line children up with their amounts.
    [Fact]
    public void TheBreakdownReconciles()
    {
        var reliefs = PcbReliefs.Itemise(
            isOku: true, spouseWorking: false, spouseDisabled: true,
            [Child(), Child(ChildStudyingLevel.DEGREE_ABROAD), Child(claim: ChildPcbDeductionLevel.NONE)]);

        Assert.Equal([2000m, 8000m, 0m], reliefs.ChildItems);
        Assert.Equal(10000m, reliefs.Children);
        Assert.Equal(
            reliefs.Individual + reliefs.DisabledIndividual +
            reliefs.Spouse + reliefs.DisabledSpouse + reliefs.Children,
            reliefs.Total);
        // 9,000 D + 7,000 DU + 4,000 S + 6,000 SU + 10,000 QC.
        Assert.Equal(36000m, reliefs.Total);
    }

    [Fact]
    public void TotalAgreesWithItemise()
    {
        var children = new[] { Child(), Child(ChildStudyingLevel.DIPLOMA_MALAYSIA) };

        Assert.Equal(
            PcbReliefs.Itemise(true, false, true, children).Total,
            PcbReliefs.Total(true, false, true, children));
    }
}
