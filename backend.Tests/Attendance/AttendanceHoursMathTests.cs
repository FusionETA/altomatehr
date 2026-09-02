using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Overtime;

namespace AltomateHR.Api.Tests.Attendance;

// Pins the worked-minutes rule: a working day counts at most the shift length,
// and anything past it needs an approved OT submission to become money.
//
// Shift used throughout: 09:00–18:00 with a 60-minute unpaid break, so the
// standard day is 480 minutes and the raw clock span of a full day is 540.
public class AttendanceHoursMathTests
{
    private const int StandardDaily = 480;   // 8h, net of the break
    private const int UnpaidBreak = 60;

    private static AttendanceHoursMath.DayContribution Day(
        int clockedMin, int breakMin = UnpaidBreak, OtDayType dayType = OtDayType.NORMAL_DAY) =>
        AttendanceHoursMath.ForDay(clockedMin, breakMin, StandardDaily, UnpaidBreak, dayType);

    [Fact]
    public void TextbookDay_CountsExactlyTheShift()
    {
        var day = Day(540);   // 09:00 → 18:00

        Assert.Equal(480, day.NormalMin);
        Assert.Equal(0, day.BeyondShiftMin);
        Assert.Equal(60, day.BreakMin);
    }

    [Fact]
    public void ClockingOutLate_CountsTheShiftAndRecordsTheOverrun()
    {
        // 08:57 → 18:05. The 8 minutes past the shift are kept, not paid.
        var day = Day(548);

        Assert.Equal(480, day.NormalMin);
        Assert.Equal(8, day.BeyondShiftMin);
    }

    [Fact]
    public void LateIn_MadeUpByLateOut_StillCountsAFullDay()
    {
        // 09:15 → 18:15: late start, but the full shift length was worked. This
        // is the whole point of capping duration rather than clamping to the
        // 09:00–18:00 window, which would have docked this day.
        var day = Day(540);

        Assert.Equal(480, day.NormalMin);
        Assert.Equal(0, day.BeyondShiftMin);
    }

    [Fact]
    public void LeavingEarly_ShowsTheShortfall()
    {
        // 09:00 → 17:00. Before breaks were deducted this read as a full day,
        // because the un-deducted lunch exactly cancelled the missing hour.
        var day = Day(480);

        Assert.Equal(420, day.NormalMin);
        Assert.Equal(0, day.BeyondShiftMin);
    }

    [Fact]
    public void ForgottenClockOut_IsContainedByTheCap()
    {
        // 09:00 → 23:00, closed by the auto-clock-out sweep. Uncapped this was
        // 175% of a day's target.
        var day = Day(840);

        Assert.Equal(480, day.NormalMin);
        Assert.Equal(300, day.BeyondShiftMin);
    }

    [Fact]
    public void NoBreakRecorded_OnALongDay_FallsBackToTheShiftBreak()
    {
        var day = Day(540, breakMin: 0);

        Assert.Equal(60, day.BreakMin);
        Assert.Equal(480, day.NormalMin);
    }

    [Fact]
    public void NoBreakRecorded_OnAShortDay_DeductsNothing()
    {
        // Under the shift length, so no assumption is made — the employee may
        // genuinely not have taken a break, and docking one would invent a
        // shortfall.
        var day = Day(300, breakMin: 0);

        Assert.Equal(0, day.BreakMin);
        Assert.Equal(300, day.NormalMin);
    }

    [Fact]
    public void RecordedBreak_WinsOverTheFallback()
    {
        var day = Day(540, breakMin: 25);

        Assert.Equal(25, day.BreakMin);
        Assert.Equal(480, day.NormalMin);
        Assert.Equal(35, day.BeyondShiftMin);
    }

    [Fact]
    public void RestDay_IsUncappedAndNeverNormalHours()
    {
        // Sat 29 Aug from the seed: 09:07 → 18:16.
        var day = Day(549, dayType: OtDayType.REST_DAY);

        Assert.Equal(0, day.NormalMin);
        Assert.Equal(489, day.RestDayMin);
        Assert.Equal(0, day.BeyondShiftMin);
    }

    [Fact]
    public void PublicHoliday_GetsItsOwnBucket()
    {
        var day = Day(540, dayType: OtDayType.PUBLIC_HOLIDAY);

        Assert.Equal(0, day.NormalMin);
        Assert.Equal(0, day.RestDayMin);
        Assert.Equal(480, day.PublicHolidayMin);
    }

    [Fact]
    public void BreakLongerThanTheDay_CannotProduceNegativeHours()
    {
        var day = Day(30, breakMin: 90);

        Assert.Equal(0, day.NormalMin);
        Assert.Equal(30, day.BreakMin);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public void NoClockedTime_ContributesNothing(int clockedMin)
    {
        var day = Day(clockedMin);

        Assert.Equal(0, day.WorkedMin);
        Assert.Equal(0, day.NormalMin);
        Assert.Equal(0, day.BreakMin);
    }
}
