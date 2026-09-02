using AltomateHR.Api.Modules.Leave;

namespace AltomateHR.Api.Tests.Leave;

// Ported from production's initialProRatedAccrual. A new joiner shouldn't have
// the whole year's leave available on day one — they earn 1/12 a month, plus a
// part-month credit for the month they joined.
public class LeaveProrationTests
{
    private const double Entitled = 12;   // a tidy 1 day per month

    private static double Accrued(string? joinDate, int year, string asOf) =>
        LeaveAccrualMath.ProRatedAccrualOnDate(
            Entitled,
            joinDate is null ? null : DateTime.Parse(joinDate),
            year,
            DateTime.Parse(asOf));

    [Fact]
    public void JoinedInAPriorYear_IsTreatedAsAFullJanuary()
    {
        // By 1 March: Jan + Feb + Mar credit = 3 days.
        Assert.Equal(3, Accrued("2024-05-10", 2026, "2026-03-01"));
    }

    [Fact]
    public void NoJoinDate_AlsoTreatedAsAFullJanuary()
    {
        Assert.Equal(3, Accrued(null, 2026, "2026-03-01"));
    }

    [Fact]
    public void JoinedMidYear_GetsAPartMonthCreditForTheJoinMonth()
    {
        // Joined 16 June (30 days) → 15 days worked → 15/30 of June's 1 day.
        // By 30 June: 0 full months crossed + 0.5 = 0.5 days.
        Assert.Equal(0.5, Accrued("2026-06-16", 2026, "2026-06-30"), 3);
    }

    [Fact]
    public void JoinedMidYear_ThenAccruesAFullChunkPerMonth()
    {
        // Same joiner by 31 August: 2 boundaries crossed (Jul, Aug) + 0.5.
        Assert.Equal(2.5, Accrued("2026-06-16", 2026, "2026-08-31"), 3);
    }

    [Fact]
    public void BeforeTheirFirstDay_NothingHasAccrued()
    {
        // Joins in June; asking in March must not hand them the June credit.
        Assert.Equal(0, Accrued("2026-06-16", 2026, "2026-03-01"));
    }

    [Fact]
    public void HiredInALaterYear_IsZero()
    {
        Assert.Equal(0, Accrued("2027-01-01", 2026, "2026-12-31"));
    }

    [Fact]
    public void AskingAboutAYearBeforeItStarts_IsZero()
    {
        Assert.Equal(0, Accrued("2024-01-01", 2026, "2025-12-31"));
    }

    [Fact]
    public void AfterTheYearEnds_TheWholeEntitlementHasAccrued()
    {
        Assert.Equal(Entitled, Accrued("2024-01-01", 2026, "2027-06-01"));
    }

    [Fact]
    public void NeverExceedsTheEntitlement()
    {
        Assert.Equal(Entitled, Accrued("2024-01-01", 2026, "2026-12-31"));
    }

    [Fact]
    public void ZeroEntitlement_AccruesNothing()
    {
        Assert.Equal(0, LeaveAccrualMath.ProRatedAccrualOnDate(
            0, DateTime.Parse("2024-01-01"), 2026, DateTime.Parse("2026-06-01")));
    }

    [Fact]
    public void JoiningOnTheLastDayOfAMonth_StillEarnsOneDayOfCredit()
    {
        // 30 June: 1 day worked of 30 → 1/30 of a month's chunk.
        Assert.Equal(1.0 / 30, Accrued("2026-06-30", 2026, "2026-06-30"), 4);
    }
}
