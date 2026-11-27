namespace AltomateHR.Api.Modules.Attendance;

// The answer to "whose attendance may this caller export".
//
// EmployeeId and EmployeeIds are alternatives, not both: one person's report
// gets the day-by-day calendar, a set of people gets the record list, and the
// export decides which on that basis. Both null means the whole org; an EMPTY
// EmployeeIds is a filter that matched nobody and must export no one.
public sealed record AttendanceExportScope(
    bool Allowed,
    string? EmployeeId,
    IReadOnlyCollection<string>? EmployeeIds);
