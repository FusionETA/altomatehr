using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Attendance.Entities;

// One clock-in/clock-out pair. A day can have several: clock out for an
// afternoon off and back in later, and each stint is its own session.
//
// The session owns the clock. AttendanceRecord is the DAY, and its
// TimeIn/TimeOut/DurationMin/Status are a roll-up recomputed from the sessions
// beneath it (see AttendanceService.RecomputeRollup) — first start, last end,
// and the SUM of the stints, so the gap between them isn't counted as worked.
//
// The per-clock evidence lives here rather than on the record for one concrete
// reason: a second clock-in would otherwise overwrite the first one's GPS and
// proof photo, and a morning spent off-site would silently disappear at lunch.
// The record still carries copies of the first clock-in's and last clock-out's
// evidence, as part of the same roll-up, so day-level views keep working.
public class AttendanceSession : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string AttendanceRecordId { get; set; } = string.Empty;   // FK → AttendanceRecord

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;       // denormalized, for direct lookups

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    // Minutes from StartedAt to EndedAt. Null while the session is open.
    public int? DurationMin { get; set; }

    // This stint's own punctuality. Lateness is judged per clock-in, so an
    // afternoon session starting after a lunch break isn't "late" against the
    // morning shift's start.
    public AttendanceStatus Status { get; set; } = AttendanceStatus.CLOCKED_IN;
    public int? LateByMin { get; set; }

    // Where and what was captured at each end of THIS stint.
    public double? ClockInLat { get; set; }
    public double? ClockInLng { get; set; }
    public double? ClockInDistanceMeters { get; set; }
    [MaxLength(400)]
    public string? ClockInPhotoUrl { get; set; }

    public double? ClockOutLat { get; set; }
    public double? ClockOutLng { get; set; }
    public double? ClockOutDistanceMeters { get; set; }
    [MaxLength(400)]
    public string? ClockOutPhotoUrl { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
