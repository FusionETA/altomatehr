using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance.Dtos;

// Response shape for a single approval event (clock-in, clock-out, break-start,
// break-end). A list of these, grouped by record/break, IS the audit trail.
public class AttendanceApprovalRequestDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string? EmployeeEmail { get; set; }
    public AttendanceApprovalKind Kind { get; set; }
    public string EventAt { get; set; } = string.Empty;   // ISO-8601 UTC — the proposed time, for an adjustment request
    public string? OriginalEventAt { get; set; }           // set only for a time-adjustment request
    public string? Reason { get; set; }                    // the employee's stated reason, for a time-adjustment request
    public AttendanceApprovalStatus ApprovalStatus { get; set; }
    public int CurrentStep { get; set; }
    public string? ReviewNotes { get; set; }
    public string? ReviewerId { get; set; }
    public string? SubmittedAt { get; set; }
    public string? DecidedAt { get; set; }
    public string AttendanceRecordId { get; set; } = string.Empty;
    public string? AttendanceSessionId { get; set; }
    public string? AttendanceBreakId { get; set; }
}

public class BulkApproveDto
{
    [Required, MinLength(1)]
    public List<string> Ids { get; set; } = [];
}

public class BulkRejectDto
{
    [Required, MinLength(1)]
    public List<string> Ids { get; set; } = [];

    public string? ReviewNotes { get; set; }
}

// What an employee sends to request a correction to their own clock-in and/or
// clock-out time. At least one of the two must be set; both may be, in one call.
public class SubmitTimeAdjustmentDto
{
    [Required, MaxLength(40)]
    public string RecordId { get; set; } = string.Empty;

    public DateTime? RequestedTimeIn { get; set; }
    public DateTime? RequestedTimeOut { get; set; }

    [Required, MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}
