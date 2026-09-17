namespace AltomateHR.Api.Modules.Attendance;

// The answer to "whose attendance may this caller export".
//
// EmployeeId and EmployeeIds are alternatives, not both: one person's report
// gets the day-by-day calendar, a set of people gets the record list, and the
// export decides which on that basis.
public sealed record AttendanceExportScope(
    bool Allowed,
    string? EmployeeId,
    IReadOnlyCollection<string>? EmployeeIds,
    string? TeamId);
