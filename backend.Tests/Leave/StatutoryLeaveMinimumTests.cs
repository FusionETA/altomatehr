using AltomateHR.Api.Modules.Leave;

namespace AltomateHR.Api.Tests.Leave;

// Employment Act 1955 s.60E (annual) and s.60F (sick, not hospitalised):
// minimums by completed years of service, measured on 1 January of the leave
// year. The entitlement used is max(configured, minimum).
public class StatutoryLeaveMinimumTests
{
    private const int LeaveYear = 2026;   // service measured at 1 Jan 2026

    private static double? Min(string code, string? joinDate) =>
        StatutoryLeaveMinimum.MinimumFor(
            code, joinDate is null ? null : DateTime.Parse(joinDate), LeaveYear);

    // ── annual leave, s.60E ──────────────────────────────────────────
    [Theory]
    [InlineData("2025-06-01", 8)]    // under 1 year
    [InlineData("2024-01-02", 8)]    // one day short of 2 years
    [InlineData("2024-01-01", 12)]   // exactly 2 years
    [InlineData("2021-01-02", 12)]   // one day short of 5 years
    [InlineData("2021-01-01", 16)]   // exactly 5 years
    [InlineData("2012-03-01", 16)]   // the 2012 joiner found in testing
    public void Annual_FollowsYearsOfService(string joinDate, double expected) =>
        Assert.Equal(expected, Min("ANNUAL", joinDate));

    // ── sick leave, s.60F ────────────────────────────────────────────
    [Theory]
    [InlineData("2025-06-01", 14)]
    [InlineData("2024-01-02", 14)]
    [InlineData("2024-01-01", 18)]   // exactly 2 years
    [InlineData("2021-01-02", 18)]
    [InlineData("2021-01-01", 22)]   // exactly 5 years
    public void Sick_FollowsYearsOfService(string joinDate, double expected) =>
        Assert.Equal(expected, Min("MEDICAL", joinDate));

    [Fact]
    public void NoJoinDate_NoFloor()
    {
        Assert.Null(Min("ANNUAL", null));
        Assert.Equal(6, StatutoryLeaveMinimum.Apply(6, "ANNUAL", null, LeaveYear));
    }

    [Fact]
    public void JoiningLaterInTheLeaveYear_CountsAsUnderTwoYears() =>
        Assert.Equal(8, Min("ANNUAL", "2026-09-01"));

    [Fact]
    public void APolicyAboveTheMinimum_KeepsItsHigherFigure()
    {
        // 5+ years → minimum 16; the policy's 20 stands.
        Assert.Equal(20, StatutoryLeaveMinimum.Apply(20, "ANNUAL", new DateTime(2012, 3, 1), LeaveYear));
        Assert.Equal(30, StatutoryLeaveMinimum.Apply(30, "MEDICAL", new DateTime(2012, 3, 1), LeaveYear));
    }

    [Fact]
    public void APolicyBelowTheMinimum_IsLiftedToIt()
    {
        Assert.Equal(16, StatutoryLeaveMinimum.Apply(14, "ANNUAL", new DateTime(2012, 3, 1), LeaveYear));
        Assert.Equal(22, StatutoryLeaveMinimum.Apply(14, "MEDICAL", new DateTime(2012, 3, 1), LeaveYear));
    }

    [Fact]
    public void ALowerManualOverride_IsLiftedToo() =>
        // An admin typing 5 for a 3-year employee still gets the Act's 12.
        Assert.Equal(12, StatutoryLeaveMinimum.Apply(5, "ANNUAL", new DateTime(2023, 1, 1), LeaveYear));

    [Theory]
    [InlineData("AL")]
    [InlineData("annual")]
    [InlineData(" ANNUAL ")]
    public void AnnualCodes_AreRecognised(string code) =>
        Assert.Equal(StatutoryLeaveMinimum.Kind.Annual, StatutoryLeaveMinimum.KindOf(code));

    [Theory]
    [InlineData("MEDICAL")]
    [InlineData("MC")]
    public void SickCodes_AreRecognised(string code) =>
        Assert.Equal(StatutoryLeaveMinimum.Kind.Sick, StatutoryLeaveMinimum.KindOf(code));

    [Theory]
    [InlineData("HOSPITALIZATION")]
    [InlineData("MATERNITY")]
    [InlineData("PATERNITY")]
    [InlineData("COMPASSIONATE")]
    [InlineData("UNPAID")]
    [InlineData(null)]
    public void OtherTypes_HaveNoFloor(string? code)
    {
        Assert.Null(StatutoryLeaveMinimum.MinimumFor(code, new DateTime(2012, 3, 1), LeaveYear));
        Assert.Equal(3, StatutoryLeaveMinimum.Apply(3, code, new DateTime(2012, 3, 1), LeaveYear));
    }

    [Fact]
    public void LeapDayJoiner_CompletesTheYearOn28February() =>
        Assert.Equal(2, StatutoryLeaveMinimum.CompletedYears(new DateTime(2024, 2, 29), new DateTime(2026, 2, 28)));
}
