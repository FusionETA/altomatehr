using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

public class PayPeriodTests
{
    [Theory]
    [InlineData(2026, 1, 31)]
    [InlineData(2026, 2, 28)]
    [InlineData(2024, 2, 29)]   // leap year
    [InlineData(2026, 4, 30)]
    public void CalendarDaysInMonth_CountsTheRealMonth(int year, int month, int expected)
    {
        Assert.Equal(expected, PayPeriod.CalendarDaysInMonth(year, month));
    }

    // The s.60I basis honours the org setting…
    [Fact]
    public void WorkingDaysForPeriod_TwentySixIsFixed()
    {
        Assert.Equal(26, PayPeriod.WorkingDaysForPeriod(2026, 1, WorkingDaysRule.TWENTY_SIX));
        Assert.Equal(26, PayPeriod.WorkingDaysForPeriod(2026, 2, WorkingDaysRule.TWENTY_SIX));
    }

    [Fact]
    public void WorkingDaysForPeriod_CalendarFollowsTheMonth()
    {
        Assert.Equal(31, PayPeriod.WorkingDaysForPeriod(2026, 1, WorkingDaysRule.CALENDAR));
        Assert.Equal(28, PayPeriod.WorkingDaysForPeriod(2026, 2, WorkingDaysRule.CALENDAR));
    }

    // ─── s.18A incomplete month ─────────────────────────────────────────

    [Fact]
    public void FullMonth_PaysTheWholeWagePeriod()
    {
        Assert.Equal(31, PayPeriod.EffectiveWorkedDays(
            2026, 1, joinDate: new DateTime(2024, 1, 1), leaveDate: null, daysInWagePeriod: 31));
    }

    [Fact]
    public void LateJoiner_CountsCalendarDays()
    {
        // 15–31 Jan inclusive = 17 days.
        Assert.Equal(17, PayPeriod.EffectiveWorkedDays(
            2026, 1, new DateTime(2026, 1, 15), null, 31));
    }

    [Fact]
    public void Leaver_CountsCalendarDays()
    {
        // 1–10 Jan inclusive = 10 days.
        Assert.Equal(10, PayPeriod.EffectiveWorkedDays(
            2026, 1, new DateTime(2024, 9, 2), new DateTime(2026, 1, 10), 31));
    }

    [Fact]
    public void JoinedAndLeftInTheSameMonth_CountsTheOverlap()
    {
        // 5–20 Jan inclusive = 16 days.
        Assert.Equal(16, PayPeriod.EffectiveWorkedDays(
            2026, 1, new DateTime(2026, 1, 5), new DateTime(2026, 1, 20), 31));
    }

    [Fact]
    public void JoinedAndLeftOnTheSameDay_CountsOneDay()
    {
        Assert.Equal(1, PayPeriod.EffectiveWorkedDays(
            2026, 1, new DateTime(2026, 1, 15), new DateTime(2026, 1, 15), 31));
    }

    // Regression: counting a Mon–Fri roster (21 days) against a 26 divisor paid
    // this employee 0.8077 of salary for missing a single day. s.18A is calendar
    // days over calendar days — 30/31.
    [Fact]
    public void JoiningOnTheSecond_Earns30Of31_NotAWeekdayFraction()
    {
        Assert.Equal(30, PayPeriod.EffectiveWorkedDays(
            2026, 1, new DateTime(2026, 1, 2), null, 31));
    }

    // ─── Off-period employees ───────────────────────────────────────────

    [Fact]
    public void JoinedAfterThePeriodEnded_IsNotOnThisRun()
    {
        Assert.Null(PayPeriod.EffectiveWorkedDays(
            2026, 1, new DateTime(2026, 2, 1), null, 31));
    }

    [Fact]
    public void LeftBeforeThePeriodStarted_IsNotOnThisRun()
    {
        Assert.Null(PayPeriod.EffectiveWorkedDays(
            2026, 2, new DateTime(2024, 1, 1), new DateTime(2026, 1, 31), 28));
    }

    // Joining on the last day, or leaving on the first, is still one paid day —
    // the boundary belongs on the run, not off it.
    [Fact]
    public void JoiningOnTheLastDayOfThePeriod_EarnsOneDay()
    {
        Assert.Equal(1, PayPeriod.EffectiveWorkedDays(
            2026, 1, new DateTime(2026, 1, 31), null, 31));
    }

    [Fact]
    public void LeavingOnTheFirstDayOfThePeriod_EarnsOneDay()
    {
        Assert.Equal(1, PayPeriod.EffectiveWorkedDays(
            2026, 1, null, new DateTime(2026, 1, 1), 31));
    }

    // The count can never exceed the wage period it is a fraction of.
    [Fact]
    public void ThePaidDaysNeverExceedTheWagePeriod()
    {
        Assert.Equal(26, PayPeriod.EffectiveWorkedDays(
            2026, 1, new DateTime(2026, 1, 2), null, daysInWagePeriod: 26));
    }
}
