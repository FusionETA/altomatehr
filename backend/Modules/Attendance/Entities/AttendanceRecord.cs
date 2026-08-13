using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Attendance.Entities;

// EF Core entity → the "AttendanceRecords" table. One row per employee per
// local business day (unique on EmployeeId + Date). Aligned with the real
// AltomateHR schema; geofence selfies, breaks/sessions, OT and edit logs get
// migrated later (strangler-fig style).
public class AttendanceRecord : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;       // FK (id only, for now)

    // Local-day key: the UTC-midnight instant of the employee's local calendar
    // date (see AttendanceTime.StartOfLocalDay). NOT a real timestamp — a pure
    // per-day bucket, matching the monolith's `date` column.
    public DateTime Date { get; set; }

    public DateTime? TimeIn { get; set; }
    public DateTime? TimeOut { get; set; }
    public int? DurationMin { get; set; }
    public int? LateByMin { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(40)]
    public string? ProjectId { get; set; }                       // FK → Projects (settings)

    public AttendanceStatus Status { get; set; } = AttendanceStatus.MISSING;

    // Geofence capture. Null when the employee clocked without granting
    // location, or the project has no geofence centre. Distance is the haversine
    // metres from the project centre at the moment of the clock event.
    public double? ClockInLat { get; set; }
    public double? ClockInLng { get; set; }
    public double? ClockInDistanceMeters { get; set; }
    public double? ClockOutLat { get; set; }
    public double? ClockOutLng { get; set; }
    public double? ClockOutDistanceMeters { get; set; }

    // Off-site proof photos (URL into the attendance photo storage). Required
    // alongside a remark when the employee clocks off-site.
    [MaxLength(1000)]
    public string? ClockInPhotoUrl { get; set; }

    [MaxLength(1000)]
    public string? ClockOutPhotoUrl { get; set; }

    public string? Notes { get; set; }    // system-captured context (off-site warnings, etc.)
    public string? Remark { get; set; }   // employee's own free-form remark

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
