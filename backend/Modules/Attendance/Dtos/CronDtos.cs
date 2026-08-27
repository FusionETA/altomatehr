namespace AltomateHR.Api.Modules.Attendance.Dtos;

// Result of one auto-clock-out sweep pass (background service or, if ever
// added, a manual admin trigger).
public class AttendanceAutoClockOutResultDto
{
    public int Inspected { get; set; }
    public int ClockedOut { get; set; }
    public int Errors { get; set; }
}

// One employee still clocked in past the warning cutoff. Detection only —
// no notification is actually sent (no Notifications module exists yet);
// this is what a background service logs and what the on-demand endpoint
// returns for an admin to check directly.
public class StillClockedInWarningDto
{
    public string EmployeeId { get; set; } = string.Empty;
    public string? EmployeeEmail { get; set; }
    public string RecordId { get; set; } = string.Empty;
    public string TimeIn { get; set; } = string.Empty;   // ISO-8601 UTC
    public int MinutesClockedIn { get; set; }
}

// The calling supervisor/admin/owner's own pending-approval count, across
// every kind (clock-in/out, break-start/end). Detection only, matching
// StillClockedInWarningDto — no notification delivery.
public class PendingApprovalDigestDto
{
    public int PendingCount { get; set; }
    public string? OldestSubmittedAt { get; set; }   // ISO-8601 UTC, null if PendingCount == 0
}
