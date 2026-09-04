using AltomateHR.Api.Modules.Attendance;

namespace AltomateHR.Api.Tests.Attendance;

// Asia/Kuala_Lumpur is UTC+8, so a 09:00 local start is 01:00 UTC. Every case
// here passes a UTC instant, which is what the record stores.
public class AttendanceLatenessTests
{
    private static DateTime Utc(int hour, int minute) =>
        new(2026, 9, 3, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void ClockingInAfterTheStart_ReportsTheOverrun()
    {
        // 10:33 local against a 09:00 start.
        Assert.Equal(93, AttendanceLateness.Minutes(Utc(2, 33), "09:00"));
    }

    [Fact]
    public void ExactlyOnTime_ReportsNothing()
    {
        // Null rather than 0: the UI shows a badge whenever there's a number, so
        // a punctual day has to come back empty to stay clean.
        Assert.Null(AttendanceLateness.Minutes(Utc(1, 0), "09:00"));
    }

    [Fact]
    public void ClockingInEarly_ReportsNothing()
    {
        Assert.Null(AttendanceLateness.Minutes(Utc(0, 45), "09:00"));
    }

    [Fact]
    public void OneMinuteLate_IsStillLate()
    {
        Assert.Equal(1, AttendanceLateness.Minutes(Utc(1, 1), "09:00"));
    }

    [Fact]
    public void TheComparisonHappensInLocalTime_NotUtc()
    {
        // The bug this guards: comparing the stored 01:00 UTC against a "09:00"
        // shift as if both were the same clock reports every punctual morning as
        // eight hours early — or, with the operands the other way round, eight
        // hours late.
        var onTime = AttendanceLateness.Minutes(Utc(1, 0), "09:00");
        var lateByAnHour = AttendanceLateness.Minutes(Utc(2, 0), "09:00");

        Assert.Null(onTime);
        Assert.Equal(60, lateByAnHour);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9am")]
    [InlineData("25:00")]
    [InlineData("09:61")]
    [InlineData("09")]
    public void WithNoUsableSchedule_ReportsNothing(string? scheduledStart)
    {
        // No schedule means no opinion. Defaulting to midnight would brand
        // everyone hours late the moment an org hasn't set its hours.
        Assert.Null(AttendanceLateness.Minutes(Utc(2, 33), scheduledStart));
    }

    [Fact]
    public void AnEarlyShift_IsMeasuredAgainstItsOwnStart()
    {
        // 07:30 local start; clocking in 07:45 local is 15 late, even though the
        // same instant would be early against a 09:00 shift.
        Assert.Equal(15, AttendanceLateness.Minutes(Utc(23, 45).AddDays(-1), "07:30"));
        Assert.Null(AttendanceLateness.Minutes(Utc(23, 45).AddDays(-1), "09:00"));
    }
}
