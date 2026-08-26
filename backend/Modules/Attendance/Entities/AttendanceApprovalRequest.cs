using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Attendance.Entities;

// One row per submittable attendance event (clock-in, clock-out, break-start,
// break-end). Replaces the old design where AttendanceRecord/AttendanceBreak
// each carried a single mutable approval slot shared by multiple events —
// that meant a later event (e.g. clock-out) silently overwrote an earlier
// event's already-decided approval (e.g. clock-in), with no trace left
// anywhere. Here, every event gets its own row: mutating one during ITS OWN
// decision never touches another event's row, so nothing is ever lost.
// Querying this table by date/employee IS the compliance audit log — no
// separate append-only log is needed on top.
public class AttendanceApprovalRequest : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;

    public AttendanceApprovalKind Kind { get; set; }

    // Always set, even for BREAK_* kinds — lets a record's full approval
    // history be fetched in one query without joining through breaks.
    [MaxLength(40)]
    public string AttendanceRecordId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string? AttendanceSessionId { get; set; }

    // Set only for BREAK_START/BREAK_END.
    [MaxLength(40)]
    public string? AttendanceBreakId { get; set; }

    // The clock-in/out or break start/end instant this request covers.
    public DateTime EventAt { get; set; }

    public AttendanceApprovalStatus ApprovalStatus { get; set; } = AttendanceApprovalStatus.PENDING;
    public int CurrentStep { get; set; }
    public string? ReviewNotes { get; set; }

    [MaxLength(40)]
    public string? ReviewerId { get; set; }   // who decided — not tracked anywhere pre-migration

    public DateTime SubmittedAt { get; set; }
    public DateTime? DecidedAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
