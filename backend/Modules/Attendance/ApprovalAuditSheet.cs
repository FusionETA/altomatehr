using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Attendance.Dtos;

namespace AltomateHR.Api.Modules.Attendance;

// The approval trail as a spreadsheet — who decided what, and how long it took.
//
// Export only: unlike AttendanceSummarySheet there is no import counterpart,
// and there shouldn't be. An approval trail is a record of decisions this system
// actually made; a file that could write rows into it would let an approval be
// asserted that nobody ever gave.
public static class ApprovalAuditSheet
{
    public const string SheetName = "Approval Trail";

    // Mirrors the on-screen column order so the file reads like the page it came
    // from, then adds the three the table has no room for: when it was decided,
    // the reviewer's notes, and the event's own timestamp.
    private static readonly string[] Headers =
    [
        "Submitted", "Employee", "Event", "Status",
        "Decided By", "Decided At", "Took (hours)", "Event At", "Review Notes",
    ];

    // PDF is A4 landscape and can't carry nine columns legibly, so it drops the
    // two an auditor reads last.
    private static readonly string[] PrintableHeaders =
    [
        "Submitted", "Employee", "Event", "Status", "Decided By", "Took (hours)",
    ];

    // Matches the frontend's EVENT_LABEL so the export and the screen name the
    // same event the same way.
    private static string EventLabel(string kind) => kind switch
    {
        "CLOCK_IN" => "Clock in",
        "CLOCK_OUT" => "Clock out",
        "BREAK_START" => "Break start",
        "BREAK_END" => "Break end",
        _ => kind,
    };

    // Pending rows carry the wait so far rather than a blank: "nobody has
    // touched this for 28 hours" is the most actionable cell in the file.
    private static string Took(double? delayMinutes) =>
        delayMinutes is null ? string.Empty : TabularSheet.Hours((int)Math.Round(delayMinutes.Value));

    public static TabularSheet Build(IReadOnlyList<ApprovalAuditEntryDto> rows, string? caption = null)
    {
        var sheet = new TabularSheet(SheetName, Headers, caption);

        foreach (var r in rows)
            sheet.AddRow(
                TabularSheet.DateTimeUtc(r.SubmittedAt),
                r.EmployeeName,
                EventLabel(r.Kind),
                r.Status,
                r.ReviewerName ?? string.Empty,
                TabularSheet.DateTimeUtc(r.DecidedAt),
                Took(r.DelayMinutes),
                TabularSheet.DateTimeUtc(r.EventAt),
                r.ReviewNotes ?? string.Empty);

        return sheet.SetSummary(Stats(rows));
    }

    public static TabularSheet BuildPrintable(
        IReadOnlyList<ApprovalAuditEntryDto> rows, string? caption = null)
    {
        var sheet = new TabularSheet(SheetName, PrintableHeaders, caption);

        foreach (var r in rows)
            sheet.AddRow(
                TabularSheet.DateTimeUtc(r.SubmittedAt),
                r.EmployeeName,
                EventLabel(r.Kind),
                r.Status,
                r.ReviewerName ?? string.Empty,
                Took(r.DelayMinutes));

        return sheet.SetSummary(Stats(rows));
    }

    // The band at the top of the PDF. Pending is called out on its own because
    // it is the half of this report that still needs someone to do something.
    private static (string, string)[] Stats(IReadOnlyList<ApprovalAuditEntryDto> rows)
    {
        var pending = rows.Count(r => string.Equals(r.Status, "PENDING", StringComparison.OrdinalIgnoreCase));
        var decided = rows.Where(r => r.DecidedAt is not null && r.DelayMinutes is not null).ToList();

        return
        [
            ("Requests", TabularSheet.Number(rows.Count)),
            ("Pending", TabularSheet.Number(pending)),
            ("Average decision time (hours)",
                decided.Count == 0
                    ? "—"
                    : TabularSheet.Hours((int)Math.Round(decided.Average(r => r.DelayMinutes!.Value)))),
        ];
    }
}
