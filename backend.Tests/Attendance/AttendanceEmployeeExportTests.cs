using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Dtos;

namespace AltomateHR.Api.Tests.Attendance;

// Narrowing the org hours summary to one person, for the per-employee report
// on the admin attendance detail page.
//
// The row filtering is the obvious half. The half worth pinning is that the
// TOTALS follow: a report headed with someone's name, showing the org's hours,
// is worse than no report — the reader has no way to tell it is not theirs.
public class AttendanceEmployeeExportTests
{
    private static HoursSummaryDto OrgSummary() => new()
    {
        Totals = new HoursBucketsDto { TotalMin = 800, ExpectedMin = 960 },
        Employees =
        [
            new() { EmployeeId = "usr-ahmad", Email = "ahmad@x.com", Buckets = new HoursBucketsDto { TotalMin = 300, ExpectedMin = 480 } },
            new() { EmployeeId = "usr-siti", Email = "siti@x.com", Buckets = new HoursBucketsDto { TotalMin = 500, ExpectedMin = 480 } },
        ],
    };

    [Fact]
    public void NarrowToEmployee_KeepsOnlyThatPersonsRow()
    {
        var narrowed = AttendanceService.NarrowToEmployee(OrgSummary(), "usr-ahmad");

        Assert.Equal("usr-ahmad", Assert.Single(narrowed.Employees).EmployeeId);
    }

    [Fact]
    public void NarrowToEmployee_ReplacesTheOrgTotalsWithTheirOwn()
    {
        var narrowed = AttendanceService.NarrowToEmployee(OrgSummary(), "usr-ahmad");

        // 300, not the org's 800 — the whole point of the narrowing.
        Assert.Equal(300, narrowed.Totals.TotalMin);
        Assert.Equal(480, narrowed.Totals.ExpectedMin);
    }

    [Fact]
    public void NarrowToEmployee_ForSomeoneWithNoHoursIsEmptyRatherThanWrong()
    {
        // No row in range is a real answer. Falling back to the org totals here
        // would be the worst outcome: a full-looking report for someone who was
        // never there.
        var narrowed = AttendanceService.NarrowToEmployee(OrgSummary(), "usr-nobody");

        Assert.Empty(narrowed.Employees);
        Assert.Equal(0, narrowed.Totals.TotalMin);
    }

    [Fact]
    public void NarrowToEmployee_DoesNotMutateTheSummaryItWasGiven()
    {
        // The org summary is reused by the caller for the daily rows; narrowing
        // in place would quietly shrink those too.
        var org = OrgSummary();

        AttendanceService.NarrowToEmployee(org, "usr-ahmad");

        Assert.Equal(2, org.Employees.Count);
        Assert.Equal(800, org.Totals.TotalMin);
    }
}
