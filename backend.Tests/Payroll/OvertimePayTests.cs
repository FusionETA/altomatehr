using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Tests.Payroll;

public class OvertimePayTests
{
    // ─── Hourly rate (EA s.60I ordinary rate of pay) ────────────────────

    [Fact]
    public void MonthlyStaff_DivideByWorkingDaysTimesDailyHours()
    {
        // 5,200 ÷ (26 × 8) = RM 25.00/hour.
        var rate = OvertimePay.DeriveHourlyRate(
            SalaryType.MONTHLY, monthlySalary: 5200m, hourlyRate: null, workingDays: 26);

        Assert.Equal(25m, rate);
    }

    [Fact]
    public void MonthlyStaff_HonourAProjectDerivedDay()
    {
        // 5,200 ÷ (26 × 7.5) = RM 26.666…/hour. Left unrounded on purpose —
        // rounding the rate before multiplying by hours loses sen on the total.
        var rate = OvertimePay.DeriveHourlyRate(
            SalaryType.MONTHLY, 5200m, null, workingDays: 26, dailyHours: 7.5m);

        Assert.Equal(26.6666666666666666666666667m, rate, precision: 10);
    }

    [Fact]
    public void MonthlyStaff_FallBackToAnEightHourDay()
    {
        var withNull = OvertimePay.DeriveHourlyRate(SalaryType.MONTHLY, 5200m, null, 26, null);
        var withZero = OvertimePay.DeriveHourlyRate(SalaryType.MONTHLY, 5200m, null, 26, 0m);

        Assert.Equal(25m, withNull);
        Assert.Equal(25m, withZero);
    }

    [Fact]
    public void HourlyStaff_UseTheirOwnRate()
    {
        var rate = OvertimePay.DeriveHourlyRate(
            SalaryType.HOURLY, monthlySalary: 5200m, hourlyRate: 18.5m, workingDays: 26);

        Assert.Equal(18.5m, rate);
    }

    [Fact]
    public void HourlyStaffWithNoRateOnFile_EarnNothingPerHour()
    {
        Assert.Equal(0m, OvertimePay.DeriveHourlyRate(SalaryType.HOURLY, null, null, 26));
    }

    [Fact]
    public void MonthlyStaffWithNoSalaryOnFile_EarnNothingPerHour()
    {
        Assert.Equal(0m, OvertimePay.DeriveHourlyRate(SalaryType.MONTHLY, null, null, 26));
    }

    // A zero or negative basis would divide by zero — refuse rather than throw.
    [Theory]
    [InlineData(0)]
    [InlineData(-26)]
    public void MonthlyStaffWithANonsensicalBasis_EarnNothingPerHour(int workingDays)
    {
        Assert.Equal(0m, OvertimePay.DeriveHourlyRate(
            SalaryType.MONTHLY, 5200m, null, workingDays));
    }

    // ─── Daily hours ────────────────────────────────────────────────────

    [Fact]
    public void ProjectHoursWin_NetOfTheLunchBreak()
    {
        // 09:00–18:00 less 60 minutes = 8 hours.
        var hours = OvertimePay.DeriveDailyHours(
            "09:00", "18:00", 60, "08:30", "17:30");

        Assert.Equal(8m, hours);
    }

    [Fact]
    public void OrgHoursFillInWhenTheProjectSetsNone()
    {
        // 08:30–17:30 less the 60-minute default = 8 hours.
        var hours = OvertimePay.DeriveDailyHours(null, null, null, "08:30", "17:30");

        Assert.Equal(8m, hours);
    }

    [Fact]
    public void AShorterLunchMakesALongerDay()
    {
        // 09:00–18:00 less 30 minutes = 8.5 hours.
        Assert.Equal(8.5m, OvertimePay.DeriveDailyHours("09:00", "18:00", 30, "09:00", "18:00"));
    }

    // A window that doesn't advance tells us nothing, so fall back to the standard
    // day rather than dividing the salary by zero hours.
    [Theory]
    [InlineData("18:00", "09:00")]
    [InlineData("09:00", "09:00")]
    [InlineData("not-a-time", "also-not")]
    public void AMalformedWindow_FallsBackToEightHours(string start, string end)
    {
        Assert.Equal(8m, OvertimePay.DeriveDailyHours(start, end, 60, "09:00", "18:00"));
    }

    // A lunch longer than the shift can't make the day negative.
    [Fact]
    public void AnAbsurdLunchClampsAtZero()
    {
        Assert.Equal(0m, OvertimePay.DeriveDailyHours("09:00", "10:00", 600, "09:00", "18:00"));
    }

    // ─── OT pay ─────────────────────────────────────────────────────────

    [Fact]
    public void EachCategoryPaysItsOwnMultiplier()
    {
        // 10h × 25 × 1.5 = 375 · 5h × 25 × 2.0 = 250 · 2h × 25 × 3.0 = 150.
        var pay = OvertimePay.Calculate(
            hourlyRate: 25m,
            otNormalHours: 10m, otRestHours: 5m, otPublicHours: 2m,
            otRateNormal: 1.5m, otRateRest: 2m, otRatePublicHoliday: 3m);

        Assert.Equal(775m, pay);
    }

    [Fact]
    public void NoOtHours_NoOtPay()
    {
        Assert.Equal(0m, OvertimePay.Calculate(25m, 0m, 0m, 0m, 1.5m, 2m, 3m));
    }

    // The rate stays unrounded until the end so fractional-hour OT doesn't drift.
    [Fact]
    public void TheTotalRoundsOnce_AtTheEnd()
    {
        var rate = OvertimePay.DeriveHourlyRate(SalaryType.MONTHLY, 4333m, null, 26);
        var pay = OvertimePay.Calculate(rate, 7.5m, 0m, 0m, 1.5m, 2m, 3m);

        // 4,333 ÷ 208 = 20.8317…; × 7.5 × 1.5 = 234.36 (not 234.34 from a
        // pre-rounded RM 20.83 rate).
        Assert.Equal(234.36m, pay);
    }
}
