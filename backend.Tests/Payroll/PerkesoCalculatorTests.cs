using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

public class PerkesoCalculatorTests
{
    private static PerkesoCalculator.SocsoInput Socso(
        SocsoScheme? scheme = SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY,
        decimal wage = 3000m,
        int periodYear = 2026,
        // May 2026 — before SKBBK exists, so the age tests aren't entangled with it.
        int periodMonth = 5,
        int? ageAtPeriodEnd = null,
        bool contributeToSkbbk = false) => new()
        {
            Wage = wage,
            Scheme = scheme,
            PeriodYear = periodYear,
            PeriodMonth = periodMonth,
            AgeAtPeriodEnd = ageAtPeriodEnd,
            ContributeToSkbbk = contributeToSkbbk,
        };

    // ─── SOCSO: the Cat 1 → Cat 2 age flip ──────────────────────────────

    [Fact]
    public void Under60InCat1_BothSidesContribute()
    {
        var result = PerkesoCalculator.CalculateSocso(Socso(ageAtPeriodEnd: 59));

        Assert.True(result.Employee > 0m);
        Assert.True(result.Employer > 0m);
    }

    // At 60 Invalidity cover ends, so the employee stops paying whatever the
    // profile still says. Mirrors the Part E flip in EpfCalculator.
    [Fact]
    public void SixtyPlusInCat1_AutoFlipsToCat2()
    {
        var flipped = PerkesoCalculator.CalculateSocso(Socso(ageAtPeriodEnd: 60));
        var cat2 = PerkesoCalculator.CalculateSocso(
            Socso(scheme: SocsoScheme.EMPLOYMENT_INJURY_ONLY, ageAtPeriodEnd: 59));

        Assert.Equal(0m, flipped.Employee);
        Assert.Equal(cat2.Employer, flipped.Employer);
    }

    [Theory]
    [InlineData(59, true)]
    [InlineData(60, false)]
    [InlineData(61, false)]
    public void TheFlipHappensExactlyAtSixty(int age, bool employeeStillPays)
    {
        var result = PerkesoCalculator.CalculateSocso(Socso(ageAtPeriodEnd: age));

        Assert.Equal(employeeStillPays, result.Employee > 0m);
    }

    [Fact]
    public void OnACat2Profile_AgeChangesNothing()
    {
        var under60 = PerkesoCalculator.CalculateSocso(
            Socso(scheme: SocsoScheme.EMPLOYMENT_INJURY_ONLY, ageAtPeriodEnd: 59));
        var over60 = PerkesoCalculator.CalculateSocso(
            Socso(scheme: SocsoScheme.EMPLOYMENT_INJURY_ONLY, ageAtPeriodEnd: 72));

        Assert.Equal(under60, over60);
    }

    // Age is optional so a caller that doesn't know it falls back to the stored
    // scheme rather than silently zeroing a deduction.
    [Fact]
    public void WithoutAnAge_TheStoredSchemeStands()
    {
        var result = PerkesoCalculator.CalculateSocso(Socso(ageAtPeriodEnd: null));

        Assert.True(result.Employee > 0m);
    }

    [Fact]
    public void NoScheme_MeansNoContributionAtAnyAge()
    {
        var result = PerkesoCalculator.CalculateSocso(Socso(scheme: null, ageAtPeriodEnd: 72));

        Assert.Equal(new PerkesoCalculator.SocsoResult(0m, 0m, 0m), result);
    }

    // The gazetted amounts themselves, at wage 3,000 (row 34).
    [Fact]
    public void SocsoReadsTheGazettedRow()
    {
        var cat1 = PerkesoCalculator.CalculateSocso(Socso(ageAtPeriodEnd: 40));

        Assert.Equal(51.65m, cat1.Employer);
        Assert.Equal(14.75m, cat1.Employee);

        var cat2 = PerkesoCalculator.CalculateSocso(
            Socso(scheme: SocsoScheme.EMPLOYMENT_INJURY_ONLY, ageAtPeriodEnd: 40));

        Assert.Equal(36.9m, cat2.Employer);
        Assert.Equal(0m, cat2.Employee);
    }

    // ─── SKBBK opt-in ───────────────────────────────────────────────────

    // July 2026 — inside phase 1, so these isolate the opt-in from the phase gate.
    private static PerkesoCalculator.SocsoInput SkbbkEra(bool contributeToSkbbk) =>
        Socso(periodMonth: 7, ageAtPeriodEnd: 35, contributeToSkbbk: contributeToSkbbk);

    [Fact]
    public void SkbbkOptedIn_Contributes()
    {
        Assert.True(PerkesoCalculator.CalculateSocso(SkbbkEra(true)).EmployeeSkbbk > 0m);
    }

    // Regression against the earlier auto-calc design, where anyone with a SOCSO
    // scheme from Jun 2026 had SKBBK fired for them whether they opted in or not.
    [Fact]
    public void SkbbkOptedOut_IsZero_AndLeavesSocsoAlone()
    {
        var result = PerkesoCalculator.CalculateSocso(SkbbkEra(false));

        Assert.Equal(0m, result.EmployeeSkbbk);
        Assert.True(result.Employee > 0m);
        Assert.True(result.Employer > 0m);
    }

    // SKBBK sits on top of PERKESO coverage — you cannot opt into it alone.
    [Fact]
    public void SkbbkWithoutASocsoScheme_IsZero()
    {
        var result = PerkesoCalculator.CalculateSocso(
            Socso(scheme: null, periodMonth: 7, contributeToSkbbk: true));

        Assert.Equal(0m, result.EmployeeSkbbk);
    }

    // The phase gate is orthogonal to the opt-in: a historical rerun must not
    // back-bill a contribution that did not exist in that period.
    [Fact]
    public void SkbbkBeforeJune2026_IsZeroEvenWhenOptedIn()
    {
        var result = PerkesoCalculator.CalculateSocso(
            Socso(periodMonth: 5, ageAtPeriodEnd: 35, contributeToSkbbk: true));

        Assert.Equal(0m, result.EmployeeSkbbk);
    }

    // One gazette column serves both categories, so a 60+ Cat 2 employee who has
    // opted in still contributes.
    [Fact]
    public void SkbbkFiresForCat2Too()
    {
        var result = PerkesoCalculator.CalculateSocso(Socso(
            scheme: SocsoScheme.EMPLOYMENT_INJURY_ONLY,
            periodMonth: 7,
            ageAtPeriodEnd: 65,
            contributeToSkbbk: true));

        Assert.True(result.EmployeeSkbbk > 0m);
    }

    // ─── EIS ────────────────────────────────────────────────────────────

    [Fact]
    public void Eis_ReadsTheGazettedRow()
    {
        var result = PerkesoCalculator.CalculateEis(3000m, contributeToEis: true);

        Assert.Equal(5.9m, result.Employer);
        Assert.Equal(5.9m, result.Employee);
    }

    [Fact]
    public void Eis_CapsAtTheCeilingRow()
    {
        var atCap = PerkesoCalculator.CalculateEis(6000m, contributeToEis: true);
        var wayAbove = PerkesoCalculator.CalculateEis(50_000m, contributeToEis: true);

        Assert.Equal(atCap, wayAbove);
        Assert.Equal(11.9m, atCap.Employee);
    }

    [Fact]
    public void Eis_NotContributing_IsZero()
    {
        var result = PerkesoCalculator.CalculateEis(3000m, contributeToEis: false);

        Assert.Equal(new PerkesoCalculator.EisResult(0m, 0m), result);
    }
}
