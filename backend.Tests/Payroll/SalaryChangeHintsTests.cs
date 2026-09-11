using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// Mid-cycle salary changes.
//
// The calculator pays ONE salary for the whole month. When a raise takes
// effect on the 15th that is wrong by the prorated delta, and which direction
// depends on whether the admin saved the new figure before or after
// generating the run. These tests pin both the direction and the amount.
public class SalaryChangeHintsTests
{
    private static SalaryChange Change(
        decimal previous = 5000m,
        decimal next = 6000m,
        int effectiveDay = 15,
        int year = 2026,
        int month = 1,
        SalaryType previousType = SalaryType.MONTHLY,
        SalaryType newType = SalaryType.MONTHLY,
        SalaryChangeReason reason = SalaryChangeReason.RAISE) => new()
        {
            Id = "chg-1",
            OrganizationId = "org-1",
            EmployeeProfileId = "emp-1",
            EffectiveDate = new DateTime(year, month, effectiveDay),
            PreviousSalaryType = previousType,
            PreviousMonthlySalary = previousType == SalaryType.MONTHLY ? previous : null,
            PreviousHourlyRate = previousType == SalaryType.HOURLY ? 20m : null,
            NewSalaryType = newType,
            NewMonthlySalary = newType == SalaryType.MONTHLY ? next : null,
            NewHourlyRate = newType == SalaryType.HOURLY ? 25m : null,
            Reason = reason,
        };

    private static SalaryChangeHints.Input Input(
        SalaryChange? change = null,
        decimal snapshot = 6000m,
        int periodYear = 2026,
        int periodMonth = 1,
        WorkingDaysRule rule = WorkingDaysRule.CALENDAR,
        IReadOnlyList<string>? existingLabels = null) => new()
        {
            PayslipId = "slip-1",
            EmployeeProfileId = "emp-1",
            EmployeeName = "Aisyah Binti Rahman",
            PayslipSnapshotMonthlySalary = snapshot,
            Change = change ?? Change(),
            PeriodYear = periodYear,
            PeriodMonth = periodMonth,
            ProrationRule = rule,
            ExistingManualLineLabels = existingLabels ?? [],
        };

    // ─── The two directions ─────────────────────────────────────────────

    // The admin saved the raise BEFORE generating, so the run paid RM 6,000
    // for all of January. The first 14 days should have been at RM 5,000.
    //   1,000 × 14 ÷ 31 = RM 451.61 to claw back.
    [Fact]
    public void SavedBeforeGenerating_IsAnOverpaymentToClawBack()
    {
        var hint = SalaryChangeHints.Compute(Input(snapshot: 6000m))!;

        Assert.Equal(SalaryChangeHints.Scenario.OVERPAID, hint.Outcome);
        Assert.Equal(451.61m, hint.Delta);
        Assert.Equal(14, hint.DaysAtOldRate);
        Assert.Equal(17, hint.DaysAtNewRate);
        Assert.Equal(PayslipLineKind.DEDUCTION, hint.SuggestedLineItem!.Kind);
        Assert.Equal("deduct_salary_adjustment", hint.SuggestedLineItem.Category);
        Assert.Equal(451.61m, hint.SuggestedLineItem.Amount);
    }

    // The admin generated first and saved the raise after, so the run paid
    // RM 5,000 for all of January. The last 17 days are owed as arrears.
    //   1,000 × 17 ÷ 31 = RM 548.39.
    [Fact]
    public void SavedAfterGenerating_IsArrearsOwed()
    {
        var hint = SalaryChangeHints.Compute(Input(snapshot: 5000m))!;

        Assert.Equal(SalaryChangeHints.Scenario.UNDERPAID, hint.Outcome);
        Assert.Equal(548.39m, hint.Delta);
        Assert.Equal(PayslipLineKind.ALLOWANCE, hint.SuggestedLineItem!.Kind);
        Assert.Equal("wages_arrears", hint.SuggestedLineItem.Category);
    }

    // The two corrections must add up to the whole delta — between them they
    // account for every day of the month.
    [Fact]
    public void TheTwoDirectionsAccountForTheWholeDelta()
    {
        var overpaid = SalaryChangeHints.Compute(Input(snapshot: 6000m))!;
        var underpaid = SalaryChangeHints.Compute(Input(snapshot: 5000m))!;

        Assert.Equal(1000m, overpaid.Delta + underpaid.Delta);
    }

    // A cut runs the same arithmetic the other way, and the amount is still
    // reported unsigned — the direction is in the scenario.
    [Fact]
    public void ADemotionIsHandledLikeARaise()
    {
        var change = Change(previous: 6000m, next: 5000m, reason: SalaryChangeReason.DEMOTION);

        var overpaid = SalaryChangeHints.Compute(Input(change, snapshot: 5000m))!;

        // The run paid the LOWER new rate all month, so the pre-change days
        // were underpaid — the employee is owed the difference.
        Assert.Equal(SalaryChangeHints.Scenario.OVERPAID, overpaid.Outcome);
        Assert.Equal(451.61m, overpaid.Delta);
        Assert.True(overpaid.Delta > 0m);
    }

    // ─── When there is nothing to do ────────────────────────────────────

    // Effective on the 1st: the whole month is at the new rate, so paying one
    // salary is correct and the banner should not nag.
    [Fact]
    public void EffectiveOnTheFirst_NeedsNoCorrection()
    {
        var hint = SalaryChangeHints.Compute(Input(Change(effectiveDay: 1)))!;

        Assert.Equal(SalaryChangeHints.Scenario.MATCHED, hint.Outcome);
        Assert.Equal(0m, hint.Delta);
        Assert.Null(hint.SuggestedLineItem);
    }

    [Fact]
    public void ASalaryThatDidNotMove_ProducesNoHint()
    {
        Assert.Null(SalaryChangeHints.Compute(Input(Change(previous: 5000m, next: 5000m))));
    }

    // A monthly-to-hourly switch is a different shape of arithmetic. Guessing
    // at it would be worse than leaving it to the admin.
    [Theory]
    [InlineData(SalaryType.HOURLY, SalaryType.MONTHLY)]
    [InlineData(SalaryType.MONTHLY, SalaryType.HOURLY)]
    [InlineData(SalaryType.HOURLY, SalaryType.HOURLY)]
    public void ASalaryTypeSwitch_ProducesNoHint(SalaryType from, SalaryType to)
    {
        Assert.Null(SalaryChangeHints.Compute(
            Input(Change(previousType: from, newType: to))));
    }

    // ─── When the engine cannot tell ────────────────────────────────────

    // The snapshot matches neither side — a second change, or a hand-edit
    // between generating and now. Surfaced so the admin sees it, but no
    // figure is suggested, because any figure would be a guess.
    [Fact]
    public void ASnapshotMatchingNeitherSide_IsUnknown()
    {
        var hint = SalaryChangeHints.Compute(Input(snapshot: 5500m))!;

        Assert.Equal(SalaryChangeHints.Scenario.UNKNOWN, hint.Outcome);
        Assert.Equal(0m, hint.Delta);
        Assert.Null(hint.SuggestedLineItem);
        // The context is still reported so the admin can do their own maths.
        Assert.Equal(5500m, hint.PayslipSnapshotMonthlySalary);
        Assert.Equal(5000m, hint.PreviousMonthlySalary);
        Assert.Equal(6000m, hint.NewMonthlySalary);
    }

    // The snapshot is a stored decimal; the comparison should not turn on the
    // last place.
    [Theory]
    [InlineData(6000.005)]
    [InlineData(5999.995)]
    public void ASenOfDriftStillMatches(decimal snapshot)
    {
        Assert.Equal(
            SalaryChangeHints.Scenario.OVERPAID,
            SalaryChangeHints.Compute(Input(snapshot: snapshot))!.Outcome);
    }

    // ─── Applying it once ───────────────────────────────────────────────

    // The marker embedded in the label is how the next page load knows the
    // correction is already on the run. Without it the admin is asked to
    // apply the same deduction every time they refresh.
    [Fact]
    public void AnAppliedHint_StopsSuggestingItself()
    {
        var applied = SalaryChangeHints.Compute(Input(
            existingLabels: [$"Mid-cycle salary change deduct {SalaryChangeHints.Marker("chg-1")}"]))!;

        Assert.True(applied.AlreadyApplied);
        Assert.Null(applied.SuggestedLineItem);
        // The delta is still reported, so the banner can say what was applied.
        Assert.Equal(451.61m, applied.Delta);
    }

    // A different change's marker must not suppress this one.
    [Fact]
    public void AnotherChangesMarker_DoesNotSuppressThisOne()
    {
        var hint = SalaryChangeHints.Compute(Input(
            existingLabels: [$"Something else {SalaryChangeHints.Marker("chg-99")}"]))!;

        Assert.False(hint.AlreadyApplied);
        Assert.NotNull(hint.SuggestedLineItem);
    }

    [Fact]
    public void TheSuggestedLabelCarriesItsOwnMarker()
    {
        var hint = SalaryChangeHints.Compute(Input())!;

        Assert.Contains(SalaryChangeHints.Marker("chg-1"), hint.SuggestedLineItem!.Label);
        // And enough human text to make sense on a payslip.
        Assert.Contains("2026-01-15", hint.SuggestedLineItem.Label);
        Assert.Contains("5000", hint.SuggestedLineItem.Label);
        Assert.Contains("6000", hint.SuggestedLineItem.Label);
    }

    // ─── The divisor, and the split ─────────────────────────────────────

    // The org's rule decides the DIVISOR. The before/after split is always
    // calendar days — the 15th is the 15th whatever basis the org pays on.
    [Fact]
    public void TheTwentySixRuleChangesTheDivisorNotTheSplit()
    {
        var hint = SalaryChangeHints.Compute(
            Input(rule: WorkingDaysRule.TWENTY_SIX, snapshot: 6000m))!;

        Assert.Equal(26, hint.TotalDaysInPeriod);
        // The split is unchanged: 14 calendar days before the 15th.
        Assert.Equal(14, hint.DaysAtOldRate);
        // 1,000 × 14 ÷ 26 = 538.46.
        Assert.Equal(538.46m, hint.Delta);
    }

    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2024, 2, 29)]   // leap year
    [InlineData(2026, 4, 30)]
    public void TheCalendarDivisorFollowsTheRealMonth(int year, int month, int expected)
    {
        var hint = SalaryChangeHints.Compute(Input(
            Change(effectiveDay: 10, year: year, month: month),
            periodYear: year, periodMonth: month))!;

        Assert.Equal(expected, hint.TotalDaysInPeriod);
    }

    // The effective date itself is paid at the NEW rate — a raise effective
    // the 15th means the 15th is the first day at the new salary.
    [Fact]
    public void TheEffectiveDateIsPaidAtTheNewRate()
    {
        var hint = SalaryChangeHints.Compute(Input(Change(effectiveDay: 15)))!;

        Assert.Equal(14, hint.DaysAtOldRate);
        Assert.Equal(17, hint.DaysAtNewRate);
        Assert.Equal(31, hint.DaysAtOldRate + hint.DaysAtNewRate);
    }

    // A change dated outside the period should not have reached this, but a
    // hint is not worth throwing over.
    [Fact]
    public void AChangeOutsideThePeriod_ProducesNoSplit()
    {
        var hint = SalaryChangeHints.Compute(Input(
            Change(effectiveDay: 15, month: 3), periodMonth: 1))!;

        Assert.Equal(0, hint.DaysAtOldRate);
        Assert.Equal(SalaryChangeHints.Scenario.MATCHED, hint.Outcome);
    }

    // ─── The history view ───────────────────────────────────────────────

    [Theory]
    [InlineData(5000, 6000, 20)]
    [InlineData(6000, 5000, -16.67)]
    [InlineData(3000, 3300, 10)]
    public void TheRaisePercentIsReported(decimal previous, decimal next, decimal expected)
    {
        Assert.Equal(expected, SalaryChangeHints.RaisePercent(Change(previous, next)));
    }

    // A percentage across a monthly-to-hourly switch means nothing.
    [Fact]
    public void NoRaisePercentAcrossASalaryTypeSwitch()
    {
        Assert.Null(SalaryChangeHints.RaisePercent(
            Change(previousType: SalaryType.HOURLY, newType: SalaryType.MONTHLY)));
    }

    [Fact]
    public void EveryReasonHasALabel()
    {
        foreach (var reason in Enum.GetValues<SalaryChangeReason>())
        {
            Assert.False(string.IsNullOrWhiteSpace(SalaryChangeHints.ReasonLabel(reason)));
        }
    }
}
