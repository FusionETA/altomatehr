using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Attendance.Dtos;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.Attendance;

// The attendance summary as a spreadsheet, plus the column contract for
// importing historical attendance records.
//
// The export carries TWO sheets — the worked-hours summary HR reports on, and
// the daily records those hours are derived from — because a summary nobody can
// audit tends to come straight back as "can you send the detail?".
public static class AttendanceSummarySheet
{
    public const string SummarySheetName = "Hours Summary";
    public const string RecordsSheetName = "Daily Records";
    public const string ImportSheetName = "Attendance Import";

    // Hours, not minutes: minutes are how we store time, hours are how payroll
    // reads it. TabularSheet.Hours does the conversion in one place.
    private static readonly string[] SummaryHeaders =
    [
        "Employee", "Employee Email", "Role",
        "Normal Hours", "Rest Day Hours", "Total Hours",
        "OT Approved Hours", "OT Pending Hours", "OT Rejected Hours",
        "Expected Hours", "Variance Hours",
    ];

    private static readonly string[] RecordHeaders =
    [
        "Employee", "Employee Email", "Date", "Clock In", "Clock Out",
        "Worked Hours", "Late By (min)", "Status", "Approval", "Project",
        "Location", "Off-site Distance (m)", "Remark", "Notes",
    ];

    // ---- Export ----

    public static TabularSheet BuildSummary(
        HoursSummaryDto summary, EmployeeDirectorySnapshot employees, string? caption = null)
    {
        var sheet = new TabularSheet(SummarySheetName, SummaryHeaders, caption);

        foreach (var row in summary.Employees.OrderBy(e => employees.NameOf(e.EmployeeId), StringComparer.OrdinalIgnoreCase))
            AppendBuckets(sheet, employees.NameOf(row.EmployeeId), row.Email ?? employees.EmailOf(row.EmployeeId),
                employees.ById(row.EmployeeId)?.Role ?? "", row.Buckets);

        // Kept OUT of the data rows: as a TotalsRow the PDF can rule it off and
        // bold it, and nothing downstream has to guess whether the last line is
        // an employee. CSV and XLSX still print it last, where HR looks first.
        if (summary.Employees.Count > 0)
            sheet.SetTotals(Buckets("TOTAL", "", "", summary.Totals));

        return sheet;
    }

    private static void AppendBuckets(
        TabularSheet sheet, string name, string email, string role, HoursBucketsDto b) =>
        sheet.AddRow(Buckets(name, email, role, b));

    private static string?[] Buckets(string name, string email, string role, HoursBucketsDto b) =>
        [
            name,
            email,
            role,
            TabularSheet.Hours(b.NormalMin),
            TabularSheet.Hours(b.RestDayMin),
            TabularSheet.Hours(b.TotalMin),
            TabularSheet.Hours(b.OtApprovedMin),
            TabularSheet.Hours(b.OtPendingMin),
            TabularSheet.Hours(b.OtRejectedMin),
            TabularSheet.Hours(b.ExpectedMin),
            // Signed on purpose: negative = under the scheduled target, which is
            // the direction a manager is actually looking for.
            TabularSheet.Hours(b.TotalMin - b.ExpectedMin),
        ];

    public static TabularSheet BuildRecords(
        IEnumerable<AttendanceRecord> records,
        IReadOnlyDictionary<string, AttendanceApprovalStatus> approvalByRecordId,
        EmployeeDirectorySnapshot employees,
        IReadOnlyDictionary<string, string> projectNames,
        string? caption = null)
    {
        var sheet = new TabularSheet(RecordsSheetName, RecordHeaders, caption);

        foreach (var r in records)
        {
            // The larger of the two clock distances: whichever end was furthest
            // off-site is the one worth reviewing.
            var distance = new[] { r.ClockInDistanceMeters, r.ClockOutDistanceMeters }
                .Where(d => d is not null)
                .Select(d => d!.Value)
                .DefaultIfEmpty(double.NaN)
                .Max();

            sheet.AddRow(
                employees.NameOf(r.EmployeeId),
                employees.EmailOf(r.EmployeeId),
                TabularSheet.Date(r.Date),
                TabularSheet.DateTimeUtc(r.TimeIn),
                TabularSheet.DateTimeUtc(r.TimeOut),
                r.DurationMin is null ? "" : TabularSheet.Hours(r.DurationMin.Value),
                r.LateByMin is null ? "" : TabularSheet.Number(r.LateByMin.Value),
                r.Status.ToString(),
                approvalByRecordId.TryGetValue(r.Id, out var approval) ? approval.ToString() : "",
                r.ProjectId is null ? "" : projectNames.GetValueOrDefault(r.ProjectId, ""),
                r.Location ?? "",
                double.IsNaN(distance) ? "" : TabularSheet.Number((int)Math.Round(distance)),
                r.Remark ?? "",
                r.Notes ?? "");
        }

        return sheet;
    }

    // ---- Export: the printable records table ----
    //
    // Ten columns instead of fourteen. The geofence distance and the system
    // Notes field go first: they're triage data for someone at a screen who can
    // click into the record, not something a reader can act on from paper.
    private static readonly string[] PrintRecordHeaders =
    [
        "Employee", "Date", "Clock In", "Clock Out", "Worked Hours",
        "Late By (min)", "Status", "Approval", "Project", "Remark",
    ];

    public static TabularSheet BuildPrintableRecords(
        IReadOnlyCollection<AttendanceRecord> records,
        IReadOnlyDictionary<string, AttendanceApprovalStatus> approvalByRecordId,
        EmployeeDirectorySnapshot employees,
        IReadOnlyDictionary<string, string> projectNames,
        string caption)
    {
        var sheet = new TabularSheet(RecordsSheetName, PrintRecordHeaders, caption);

        foreach (var r in records)
        {
            sheet.AddRow(
                employees.NameOf(r.EmployeeId) is { Length: > 0 } name
                    ? name
                    : employees.EmailOf(r.EmployeeId),
                TabularSheet.Date(r.Date),
                Time(r.TimeIn),
                Time(r.TimeOut),
                r.DurationMin is null ? "" : TabularSheet.Hours(r.DurationMin.Value),
                r.LateByMin is null ? "" : TabularSheet.Number(r.LateByMin.Value),
                r.Status.ToString(),
                approvalByRecordId.TryGetValue(r.Id, out var approval) ? approval.ToString() : "",
                r.ProjectId is null ? "" : projectNames.GetValueOrDefault(r.ProjectId, ""),
                r.Remark ?? "");
        }

        var workedMinutes = records.Sum(r => r.DurationMin ?? 0);
        sheet.SetTotals(
            $"{records.Count} day(s)", "", "", "TOTAL",
            TabularSheet.Hours(workedMinutes), "", "", "", "", "");

        return sheet;
    }

    // The date is already its own column, so printing the full timestamp again
    // in Clock In / Clock Out just wastes width.
    private static string Time(DateTime? value) =>
        value is null
            ? ""
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc).ToString("HH:mm");

    // ---- Import ----
    //
    // Daily records, one row per employee per day — matching the unique key the
    // table already enforces (EmployeeId + Date), which is also what makes the
    // import idempotent for free.
    //
    // Geofence coordinates and selfies are NOT importable: they're evidence
    // captured at the moment of the clock event, and back-filling them would
    // manufacture proof that nobody actually collected.
    public static readonly IReadOnlyList<TabularColumn> ImportColumns =
    [
        new("employeeEmail", "Employee Email", true, "ahmad@company.com",
            ["email", "employee", "staff email"]),
        new("employeeName", "Employee Name", false, "Ahmad Ali",
            ["name", "full name", "member"]),
        new("date", "Date", true, "2026-01-15",
            ["work date", "attendance date", "day"]),
        new("clockIn", "Clock In", false, "09:03",
            ["time in", "in", "start"]),
        new("clockOut", "Clock Out", false, "18:12",
            ["time out", "out", "end"]),
        new("status", "Status", false, "CLOCKED_OUT"),
        new("project", "Project", false, "Head Office",
            ["project name", "site"]),
        new("location", "Location", false, "Head Office"),
        new("remark", "Remark", false, "Migrated from Jibble",
            ["note", "notes", "comment"]),
    ];

    public static TabularSheet BuildImportTemplate() =>
        TabularTemplate.Build(ImportSheetName, ImportColumns);
}
