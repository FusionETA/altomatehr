using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Attendance.Entities;

// A break taken during an AttendanceSession. Goes through the same approval
// router as clock-in/clock-out (ApprovalStatus/CurrentStep, ApprovalModule.
// ATTENDANCE) — ending a break resets those fields back to PENDING/step 0,
// mirroring how ClockOutAsync already resets AttendanceRecord's approval
// fields on top of clock-in's decision.
public class AttendanceBreak : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string AttendanceSessionId { get; set; } = string.Empty;   // FK → AttendanceSession

    [MaxLength(40)]
    public string AttendanceRecordId { get; set; } = string.Empty;    // denormalized — flat "load breaks for day" query, no join

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;            // denormalized — read directly by the approval router

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    // No DurationMin column — always computed on the fly (StartedAt/EndedAt diff).

    // GPS capture, optional, gated by EmployeePolicy.CaptureLocationOnBreakStart/End.
    public double? StartLat { get; set; }
    public double? StartLng { get; set; }
    public double? EndLat { get; set; }
    public double? EndLng { get; set; }

    [MaxLength(1000)]
    public string? Remark { get; set; }

    public AttendanceApprovalStatus ApprovalStatus { get; set; } = AttendanceApprovalStatus.PENDING;
    public int CurrentStep { get; set; }
    public string? ReviewNotes { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
