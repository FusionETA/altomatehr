using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Attendance.Entities;

// Reduced-scope child of AttendanceRecord — one session per clock-in/clock-out
// pair, created on ClockInAsync success and closed on ClockOutAsync success.
// AttendanceRecord's own TimeIn/TimeOut/DurationMin remain the source of
// truth; StartedAt/EndedAt here just mirror them so breaks have a real
// session parent. Does NOT implement the real app's full multi-session-per-day
// model (no orphan-session recovery, no blocked re-clock-in) — future work if
// genuinely needed.
public class AttendanceSession : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string AttendanceRecordId { get; set; } = string.Empty;   // FK → AttendanceRecord

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;       // denormalized, for direct lookups

    public DateTime StartedAt { get; set; }   // mirrors AttendanceRecord.TimeIn
    public DateTime? EndedAt { get; set; }    // mirrors AttendanceRecord.TimeOut when set

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
