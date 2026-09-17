using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Attendance.Entities;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.Attendance;

// One row per CALENDAR DAY, not per attendance record.
//
// The records-only export listed the days someone clocked in and said nothing
// about the rest, so a month containing two public holidays and a week of
// annual leave read as eight days absent. Absence was ambiguous: a missed
// clock-in and a public holiday were both simply "no row".
//
// This is the shape the previous system's attendance PDF has always had —
// every day accounted for, and each one labelled with WHY it looks the way it
// does. A blank here means "we say nothing happened", which is a claim the
// reader can act on.
public static class AttendanceCalendarSheet
{
    public const string SheetName = "Daily Calendar";

    // Approval and Remark are here rather than on a second sheet: this used to
    // be printed alongside a Daily Records table carrying the same rows, so a
    // one-person report ran to three pages, two of which said the same thing.
    private static readonly string[] Headers =
    [
        "Date", "Day", "Type", "Clock In", "Clock Out",
        "Worked Hours", "Late By (min)", "Status", "Approval", "Project",
        "Remark", "Detail",
    ];

    // What a day turned out to be. Ordered by precedence: a public holiday
    // that someone also took leave on is still a public holiday, and the
    // holiday is why nobody expected them.
    public enum DayKind { Holiday, RestDay, Leave, Worked, Missing }

    public sealed record Day(
        DateTime Date,
        DayKind Kind,
        AttendanceRecord? Record,
        // The holiday's name, or the leave type — whatever explains the row.
        string? Detail);

    public static TabularSheet Build(
        IReadOnlyCollection<Day> days,
        EmployeeRowIndex employees,
        IReadOnlyDictionary<string, string> projectNames,
        IReadOnlyDictionary<string, AttendanceApprovalStatus> approvalByRecordId,
        TimeZoneInfo zone,
        string? caption = null,
        // Included only for a multi-employee export; a single person's report
        // repeats their own name on every one of thirty rows for nothing.
        bool includeEmployee = false)
    {
        var headers = includeEmployee ? new[] { "Employee" }.Concat(Headers).ToArray() : Headers;
        var sheet = new TabularSheet(SheetName, headers, caption);

        foreach (var day in days)
        {
            var r = day.Record;
            var cells = new List<string>();

            if (includeEmployee)
                cells.Add(r is null ? "" : employees.NameOf(r.EmployeeId));

            cells.AddRange(
            [
                TabularSheet.Date(day.Date),
                day.Date.ToString("ddd"),
                Label(day.Kind),
                // Local, not UTC: the date column already says which day this
                // is, so repeating it beside the time wasted half the column
                // and read as midnight for anyone who arrived at 8am.
                TabularSheet.LocalTime(r?.TimeIn, zone),
                TabularSheet.LocalTime(r?.TimeOut, zone),
                r?.DurationMin is null ? "" : TabularSheet.Hours(r.DurationMin.Value),
                r?.LateByMin is null ? "" : TabularSheet.Number(r.LateByMin.Value),
                // A holiday or rest day has no attendance status to report, and
                // printing the enum's default there would invent one.
                r is null ? "" : r.Status.ToString(),
                r is not null && approvalByRecordId.TryGetValue(r.Id, out var approval)
                    ? approval.ToString()
                    : "",
                r?.ProjectId is null ? "" : projectNames.GetValueOrDefault(r.ProjectId, ""),
                r?.Remark ?? "",
                day.Detail ?? "",
            ]);

            sheet.AddRow([.. cells]);
        }

        return sheet;
    }

    private static string Label(DayKind kind) => kind switch
    {
        DayKind.Holiday => "Public holiday",
        DayKind.RestDay => "Rest day",
        DayKind.Leave => "On leave",
        DayKind.Worked => "Worked",
        // Named rather than left blank: a working day with no record is the
        // one row in this file anybody needs to chase.
        _ => "No record",
    };
}
