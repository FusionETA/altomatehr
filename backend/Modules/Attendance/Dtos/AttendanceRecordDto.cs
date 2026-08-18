using AltomateHR.Api.Modules.Attendance.Entities;

namespace AltomateHR.Api.Modules.Attendance.Dtos;

// Response shape for an attendance record. Instants are ISO-8601 UTC strings
// (with a trailing "Z") so the browser parses them unambiguously; `Date` is the
// plain local-day key (yyyy-MM-dd).
public class AttendanceRecordDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeId { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;   // yyyy-MM-dd (local day)
    public string? TimeIn { get; set; }                // ISO-8601 UTC
    public string? TimeOut { get; set; }
    public int? DurationMin { get; set; }
    public int? LateByMin { get; set; }
    public string? Location { get; set; }
    public string? ProjectId { get; set; }
    public double? ClockInLat { get; set; }
    public double? ClockInLng { get; set; }
    public double? ClockInDistanceMeters { get; set; }
    public double? ClockOutLat { get; set; }
    public double? ClockOutLng { get; set; }
    public double? ClockOutDistanceMeters { get; set; }
    public string? ClockInPhotoUrl { get; set; }
    public string? ClockOutPhotoUrl { get; set; }
    public AttendanceStatus Status { get; set; }
    public AttendanceApprovalStatus ApprovalStatus { get; set; }
    public int CurrentStep { get; set; }
    public string? EmployeeEmail { get; set; }
    public string? Notes { get; set; }
    public string? Remark { get; set; }
    public string? ReviewNotes { get; set; }
    public string? SubmittedAt { get; set; }
    public string? DecidedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
}

public class RejectAttendanceDto
{
    public string? ReviewNotes { get; set; }
}
