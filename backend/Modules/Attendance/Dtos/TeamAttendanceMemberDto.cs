namespace AltomateHR.Api.Modules.Attendance.Dtos;

// One member of a supervisor's team, for today, tagged with the project and
// team they belong to.
//
// The project is what the supervisor switches between: someone leading crews on
// two sites wants to look at one site at a time, so every member carries the
// project they're under. A person on teams in two projects appears once per
// project, which is correct — they're a different crew member in each.
//
// `Record` is null when they have no attendance row yet — they haven't clocked
// in. That's a first-class state, not missing data: "hasn't started" is exactly
// what a supervisor is scanning for, so the member is listed either way rather
// than disappearing from the list until they clock in.
public class TeamAttendanceMemberDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string? EmployeeEmail { get; set; }
    public string ProjectId { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string TeamId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public AttendanceRecordDto? Record { get; set; }
}
