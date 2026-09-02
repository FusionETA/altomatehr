using System.Text;
using AltomateHR.Api.Common;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Attendance;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Tests.Support;

namespace AltomateHR.Api.Tests.Attendance;

// The printable half of the attendance export. The spreadsheet path is covered
// by the tabular tests; these pin the PDF-specific choices — a narrower table,
// a totals line, and rendering at all when a range matches nothing.
public class AttendanceExportPdfTests
{
    private static readonly EmployeeIdentity Ahmad =
        new("usr-ahmad", "ahmad@x.com", "Ahmad Ali", "Employee");

    private static AttendanceRecord Day(string id, int day, int? durationMin = 450) => new()
    {
        Id = id,
        OrganizationId = "org-1",
        EmployeeId = "usr-ahmad",
        Date = new DateTime(2026, 1, day),
        TimeIn = new DateTime(2026, 1, day, 9, 3, 0, DateTimeKind.Utc),
        TimeOut = durationMin is null ? null : new DateTime(2026, 1, day, 17, 30, 0, DateTimeKind.Utc),
        DurationMin = durationMin,
        Status = AttendanceStatus.CLOCKED_OUT,
        Remark = "On site",
    };

    private static TabularSheet Printable(params AttendanceRecord[] records) =>
        AttendanceSummarySheet.BuildPrintableRecords(
            records,
            new Dictionary<string, AttendanceApprovalStatus>(),
            EmployeeDirectoryTestFactory.Snapshot([Ahmad]),
            new Dictionary<string, string>(),
            "1 Jan 2026 - 31 Jan 2026");

    [Fact]
    public void PrintableRecordsUseTheNarrowerColumnSet()
    {
        var spreadsheet = AttendanceSummarySheet.BuildRecords(
            [Day("r1", 5)],
            new Dictionary<string, AttendanceApprovalStatus>(),
            EmployeeDirectoryTestFactory.Snapshot([Ahmad]),
            new Dictionary<string, string>());

        var printable = Printable(Day("r1", 5));

        Assert.True(printable.Headers.Count < spreadsheet.Headers.Count);
        Assert.True(printable.Headers.Count <= TabularPdfRenderer.ComfortableColumnCount);

        // Screen-triage columns are the ones dropped.
        Assert.DoesNotContain("Off-site Distance (m)", printable.Headers);
        Assert.DoesNotContain("Notes", printable.Headers);
    }

    // The date already has its own column; repeating it inside Clock In just
    // eats width that the remark needs.
    [Fact]
    public void PrintableClockTimesAreTimeOnly()
    {
        var row = Assert.Single(Printable(Day("r1", 5)).Rows);

        Assert.Equal("2026-01-05", row[1]);
        Assert.Equal("09:03", row[2]);
        Assert.Equal("17:30", row[3]);
    }

    [Fact]
    public void PrintableRecordsCarryTotalWorkedHours()
    {
        var sheet = Printable(Day("r1", 5, 450), Day("r2", 6, 510));

        var totals = string.Join("|", sheet.TotalsRow!);
        Assert.Contains("2 day(s)", totals);
        Assert.Contains("16.00", totals);   // 450 + 510 minutes
    }

    [Fact]
    public void ADayWithNoClockOutContributesNothingToTheTotal()
    {
        var sheet = Printable(Day("r1", 5, 450), Day("r2", 6, durationMin: null));

        Assert.Contains("7.50", string.Join("|", sheet.TotalsRow!));
    }

    [Fact]
    public void SummaryTotalsAreATotalsRowNotADataRow()
    {
        var summary = new HoursSummaryDto
        {
            Totals = new HoursBucketsDto { TotalMin = 960, ExpectedMin = 900 },
            Employees =
            [
                new EmployeeHoursSummaryDto
                {
                    EmployeeId = "usr-ahmad",
                    Email = "ahmad@x.com",
                    Buckets = new HoursBucketsDto { TotalMin = 960, ExpectedMin = 900 },
                },
            ],
        };

        var sheet = AttendanceSummarySheet.BuildSummary(
            summary, EmployeeDirectoryTestFactory.Snapshot([Ahmad]));

        Assert.Single(sheet.Rows);                       // the employee, not the total
        Assert.Equal("TOTAL", sheet.TotalsRow![0]);
        Assert.Equal("1.00", sheet.TotalsRow[^1]);       // variance: 960 - 900 = 60 min
    }

    [Fact]
    public void RendersBothSheetsAsARealPdf()
    {
        var bytes = TabularWriter.Write(
            [
                AttendanceSummarySheet.BuildSummary(new HoursSummaryDto(), EmployeeDirectoryTestFactory.Snapshot([Ahmad])),
                Printable(Day("r1", 5), Day("r2", 6)),
            ],
            TabularFormat.Pdf,
            new TabularPdfHeader("Acme Sdn Bhd", "Attendance Report"));

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes.Take(4).ToArray()));
    }

    [Fact]
    public void RendersWhenTheRangeMatchedNothing()
    {
        var bytes = TabularWriter.Write(
            [
                AttendanceSummarySheet.BuildSummary(new HoursSummaryDto(), EmployeeDirectoryTestFactory.Snapshot([])),
                Printable(),
            ],
            TabularFormat.Pdf,
            new TabularPdfHeader("Acme Sdn Bhd", "Attendance Report"));

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes.Take(4).ToArray()));
    }
}
