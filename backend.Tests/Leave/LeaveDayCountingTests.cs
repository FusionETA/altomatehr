using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Tests.Leave;

// The rule that decides what a leave request actually costs. Ported from
// production's computeTotalDays; before this, V2 counted calendar days and
// silently overcharged anyone whose leave spanned a weekend.
public class LeaveDayCountingTests
{
    private static readonly HashSet<int> MonToFri = [1, 2, 3, 4, 5];
    private static readonly HashSet<DateTime> NoHolidays = [];

    private static double Count(string from, string to,
        LeaveDuration duration = LeaveDuration.FULL_DAY,
        IReadOnlySet<int>? working = null,
        IReadOnlySet<DateTime>? holidays = null) =>
        LeaveAccrualMath.ComputeTotalDays(
            DateTime.Parse(from), DateTime.Parse(to), duration,
            working ?? MonToFri, holidays ?? NoHolidays);

    [Fact]
    public void FridayToMonday_Costs2Days_Not4()
    {
        // 2026-09-04 is a Friday, 2026-09-07 the Monday after.
        Assert.Equal(2, Count("2026-09-04", "2026-09-07"));
    }

    [Fact]
    public void SingleWorkingDay_Costs1()
    {
        Assert.Equal(1, Count("2026-09-02", "2026-09-02"));   // Wednesday
    }

    [Fact]
    public void AWeekendOnlyRequest_CostsNothing()
    {
        // Saturday to Sunday — the caller should be told there are no working days.
        Assert.Equal(0, Count("2026-09-05", "2026-09-06"));
    }

    [Fact]
    public void PublicHolidaysAreSkipped()
    {
        // Mon-Fri with the Wednesday a public holiday.
        var holiday = new HashSet<DateTime> { new(2026, 9, 2) };
        Assert.Equal(5, Count("2026-08-31", "2026-09-04"));
        Assert.Equal(4, Count("2026-08-31", "2026-09-04", holidays: holiday));
    }

    [Fact]
    public void HalfDayIsAlwaysHalf_RegardlessOfTheRange()
    {
        Assert.Equal(0.5, Count("2026-09-02", "2026-09-02", LeaveDuration.MORNING));
        Assert.Equal(0.5, Count("2026-09-02", "2026-09-02", LeaveDuration.AFTERNOON));
    }

    [Fact]
    public void EndBeforeStart_CostsNothing()
    {
        Assert.Equal(0, Count("2026-09-04", "2026-09-01"));
    }

    [Fact]
    public void ASixDayWorkingWeekCountsSaturday()
    {
        // Mon-Sat orgs are common in Malaysia.
        HashSet<int> monToSat = [1, 2, 3, 4, 5, 6];
        Assert.Equal(3, Count("2026-09-04", "2026-09-07", working: monToSat));   // Fri, Sat, Mon
    }

    [Theory]
    [InlineData(null, 5)]           // null → Mon-Fri
    [InlineData("", 5)]             // blank → Mon-Fri
    [InlineData("nonsense", 5)]     // unparseable → Mon-Fri
    [InlineData("1,2,3,4,5,6", 6)]
    [InlineData("1,2,3", 3)]
    [InlineData("9,10", 5)]         // all out of range → Mon-Fri
    public void WorkingDaysParsing_FallsBackToMonToFri(string? csv, int expectedCount)
    {
        Assert.Equal(expectedCount, LeaveAccrualMath.ParseWorkingDays(csv).Count);
    }

    [Theory]
    [InlineData("2026-09-07", 1)]   // Monday
    [InlineData("2026-09-12", 6)]   // Saturday
    [InlineData("2026-09-13", 7)]   // Sunday
    public void IsoWeekday_MondayIs1_SundayIs7(string date, int expected)
    {
        Assert.Equal(expected, LeaveAccrualMath.IsoWeekday(DateTime.Parse(date)));
    }
}
