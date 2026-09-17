using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Tests.Attendance;

// The export used to list only the days an attendance record existed for, so
// a month with public holidays and a week of leave read as days absent —
// "no row" meant a missed clock-in and a public holiday equally. The calendar
// enumerates every day and says which it was.
public class AttendanceCalendarExportTests
{
    private static readonly EmployeeRowIndex NoEmployees =
        AltomateHR.Api.Tests.Support.EmployeeDirectoryTestFactory.Snapshot([]);
    private static readonly Dictionary<string, string> NoProjects = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, AttendanceApprovalStatus> NoApprovals =
        new(StringComparer.Ordinal);
    private static readonly TimeZoneInfo Kl =
        TimeZoneInfo.FindSystemTimeZoneById(AttendanceTime.DefaultTimeZone);

    [Fact]
    public void Build_EmitsARowForEveryDayInTheRange_NotJustTheWorkedOnes()
    {
        // Mon-Fri, one record. Four of the five days have nothing behind them
        // and must still appear.
        var days = new List<AttendanceCalendarSheet.Day>
        {
            Worked(new(2026, 9, 14)),
            Missing(new(2026, 9, 15)),
            Missing(new(2026, 9, 16)),
            Missing(new(2026, 9, 17)),
            Missing(new(2026, 9, 18)),
        };

        var sheet = AttendanceCalendarSheet.Build(days, NoEmployees, NoProjects, NoApprovals, Kl);

        Assert.Equal(5, sheet.Rows.Count);
    }

    [Theory]
    [InlineData(AttendanceCalendarSheet.DayKind.Holiday, "Public holiday")]
    [InlineData(AttendanceCalendarSheet.DayKind.RestDay, "Rest day")]
    [InlineData(AttendanceCalendarSheet.DayKind.Leave, "On leave")]
    [InlineData(AttendanceCalendarSheet.DayKind.Worked, "Worked")]
    [InlineData(AttendanceCalendarSheet.DayKind.Missing, "No record")]
    public void Build_NamesWhatEachDayWas(AttendanceCalendarSheet.DayKind kind, string expected)
    {
        var day = new AttendanceCalendarSheet.Day(new(2026, 9, 14), kind, null, null);

        var sheet = AttendanceCalendarSheet.Build([day], NoEmployees, NoProjects, NoApprovals, Kl);

        // Type is the third column: Date, Day, Type.
        Assert.Equal(expected, sheet.Rows[0][2]);
    }

    [Fact]
    public void Build_CarriesTheReasonAsDetail()
    {
        var days = new List<AttendanceCalendarSheet.Day>
        {
            new(new(2026, 9, 16), AttendanceCalendarSheet.DayKind.Holiday, null, "Malaysia Day"),
            new(new(2026, 9, 17), AttendanceCalendarSheet.DayKind.Leave, null, "Annual Leave"),
        };

        var sheet = AttendanceCalendarSheet.Build(days, NoEmployees, NoProjects, NoApprovals, Kl);

        // Detail is last.
        Assert.Equal("Malaysia Day", sheet.Rows[0][^1]);
        Assert.Equal("Annual Leave", sheet.Rows[1][^1]);
    }

    [Fact]
    public void Build_LeavesTheStatusBlankOnADayWithNoRecord()
    {
        // The enum's default would read as a real attendance status and
        // invent a fact about a day nobody was expected to work.
        var day = new AttendanceCalendarSheet.Day(
            new(2026, 9, 16), AttendanceCalendarSheet.DayKind.Holiday, null, "Malaysia Day");

        var sheet = AttendanceCalendarSheet.Build([day], NoEmployees, NoProjects, NoApprovals, Kl);

        // Date, Day, Type, Clock In, Clock Out, Worked Hours, Late By, Status
        Assert.Equal("", sheet.Rows[0][7]);
    }

    [Fact]
    public void Build_PrintsLocalWallClockTime_NotUtc()
    {
        // Stored 00:51 UTC is 08:51 in Kuala Lumpur. The PDF said 00:51, which
        // to the person holding it reads as arriving just before one in the
        // morning.
        var record = new AttendanceRecord
        {
            Id = "rec-1",
            EmployeeId = "emp-1",
            Date = new(2026, 9, 1),
            TimeIn = new(2026, 9, 1, 0, 51, 0, DateTimeKind.Utc),
            TimeOut = new(2026, 9, 1, 9, 45, 0, DateTimeKind.Utc),
            Status = AttendanceStatus.ON_TIME,
        };
        var day = new AttendanceCalendarSheet.Day(
            new(2026, 9, 1), AttendanceCalendarSheet.DayKind.Worked, record, null);

        var sheet = AttendanceCalendarSheet.Build([day], NoEmployees, NoProjects, NoApprovals, Kl);

        // Date, Day, Type, Clock In, Clock Out
        Assert.Equal("08:51", sheet.Rows[0][3]);
        Assert.Equal("17:45", sheet.Rows[0][4]);
    }

    [Fact]
    public void Build_OmitsTheEmployeeColumnForOnePersonsReport()
    {
        var day = Worked(new(2026, 9, 14));

        var single = AttendanceCalendarSheet.Build([day], NoEmployees, NoProjects, NoApprovals, Kl);
        var many = AttendanceCalendarSheet.Build(
            [day], NoEmployees, NoProjects, NoApprovals, Kl, includeEmployee: true);

        Assert.Equal("Date", single.Headers[0]);
        Assert.Equal("Employee", many.Headers[0]);
        Assert.Equal(single.Headers.Count + 1, many.Headers.Count);
    }

    // ---- helpers ----

    private static AttendanceCalendarSheet.Day Worked(DateTime date) =>
        new(date, AttendanceCalendarSheet.DayKind.Worked, new AttendanceRecord
        {
            Id = $"rec-{date:yyyyMMdd}",
            EmployeeId = "emp-1",
            Date = date,
            TimeIn = date.AddHours(9),
            TimeOut = date.AddHours(18),
            DurationMin = 540,
            Status = AttendanceStatus.ON_TIME,
        }, null);

    private static AttendanceCalendarSheet.Day Missing(DateTime date) =>
        new(date, AttendanceCalendarSheet.DayKind.Missing, null, null);
}
