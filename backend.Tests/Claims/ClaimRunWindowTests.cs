using AltomateHR.Api.Modules.Claims;

namespace AltomateHR.Api.Tests.Claims;

// The cutoff arithmetic, which decides which month's run a claim gets paid in.
// Month boundaries are where this goes wrong, so that is what these pin down.
public class ClaimRunWindowTests
{
    [Fact]
    public void TheRunIsNotTheCalendarMonth()
    {
        var run = ClaimRunWindow.For("2026-09", cutoffDay: 25, now: new DateTime(2026, 9, 7));

        // 26 Aug through 25 Sep — the whole point of a cutoff.
        Assert.Equal(new DateTime(2026, 8, 26), run.From);
        Assert.Equal(new DateTime(2026, 9, 26), run.To);   // exclusive
        Assert.Equal(new DateTime(2026, 9, 25), run.CutoffDate);
        Assert.Equal("2026-09", run.Label);
    }

    [Fact]
    public void AClaimSubmittedOnTheCutoffDayIsInTheRun()
    {
        var run = ClaimRunWindow.For("2026-09", 25, new DateTime(2026, 9, 7));
        var onTheCutoff = new DateTime(2026, 9, 25, 23, 59, 0);

        Assert.True(onTheCutoff >= run.From && onTheCutoff < run.To);
    }

    [Fact]
    public void AClaimSubmittedTheDayAfterTheCutoffIsNot()
    {
        var run = ClaimRunWindow.For("2026-09", 25, new DateTime(2026, 9, 7));

        Assert.False(new DateTime(2026, 9, 26) < run.To);
    }

    [Fact]
    public void TheDayAfterACutoffOpensTheNextRun()
    {
        // 26 Sep: September's run has closed, so the open run is October's.
        var run = ClaimRunWindow.For(null, 25, new DateTime(2026, 9, 26));

        Assert.Equal("2026-10", run.Label);
        Assert.Equal(new DateTime(2026, 10, 25), run.CutoffDate);
    }

    [Fact]
    public void OnTheCutoffDayItselfTheRunIsStillOpen()
    {
        var run = ClaimRunWindow.For(null, 25, new DateTime(2026, 9, 25));

        Assert.Equal("2026-09", run.Label);
    }

    [Fact]
    public void AJanuaryRunReachesBackIntoThePreviousYear()
    {
        var run = ClaimRunWindow.For("2027-01", 25, new DateTime(2027, 1, 10));

        Assert.Equal(new DateTime(2026, 12, 26), run.From);
        Assert.Equal(new DateTime(2027, 1, 26), run.To);
    }

    [Fact]
    public void AMarchRunReachesBackIntoFebruary()
    {
        // The short month: 29 Feb 2028 exists, 2026's does not. Day 28 is the
        // highest the setting allows precisely so this always resolves.
        var run = ClaimRunWindow.For("2026-03", 28, new DateTime(2026, 3, 5));

        Assert.Equal(new DateTime(2026, 2, 28).AddDays(1), run.From);
        Assert.Equal(new DateTime(2026, 3, 29), run.To);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(31)]
    public void AnOutOfRangeCutoffIsClampedRatherThanThrowing(int day)
    {
        // The API validates 1-28, so this only fires on data that predates the
        // setting. Clamping keeps the export working instead of 500ing on it.
        var run = ClaimRunWindow.For("2026-09", day, new DateTime(2026, 9, 7));

        Assert.InRange(run.CutoffDate.Day, 1, 28);
    }

    [Fact]
    public void AnUnparseableMonthFallsBackToTheOpenRun()
    {
        foreach (var bad in new[] { "", "2026", "not-a-month", "2026-13" })
        {
            var run = ClaimRunWindow.For(bad, 25, new DateTime(2026, 9, 7));
            Assert.Equal("2026-09", run.Label);
        }
    }
}
