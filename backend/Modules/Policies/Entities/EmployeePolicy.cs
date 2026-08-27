using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Policies.Entities;

// A named bundle of rules assigned to employees ("Full-time", "Part-time"…).
// Exactly one policy per org is the default (used for new/unassigned employees).
// Drives module access, attendance enforcement, OT/pay classification, and —
// via PolicyLeaveEntitlement — per-policy leave entitlements.
//
// The monolith's EmployeePolicy has more knobs (IP allowlist, per-event GPS
// capture, auto-clock-out, OT rate multipliers). Those land with the OT/payroll
// pass; this carries the fields that mean something to the modules we've built.
public class EmployeePolicy : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? Description { get; set; }

    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }

    // Module access.
    public bool CanAccessAttendance { get; set; } = true;
    public bool CanAccessClaims { get; set; } = true;
    public bool CanAccessLeave { get; set; } = true;

    // Attendance enforcement.
    public bool RequireGeofence { get; set; } = true;         // must be inside the project geofence to clock
    public bool RequireSelfie { get; set; }                   // selfie required on every clock-in
    public bool RequireClockOutSelfie { get; set; }           // selfie required on clock-out
    public bool CaptureLocationOnBreakStart { get; set; } = true;   // capture GPS when starting a break
    public bool CaptureLocationOnBreakEnd { get; set; } = true;     // capture GPS when ending a break

    // Classification / OT (rates arrive with the OT pass).
    public SalaryType SalaryType { get; set; } = SalaryType.HOURLY;
    public bool OtEnabled { get; set; } = true;
    public int OtDailyThresholdMinutes { get; set; } = 480;   // minutes/day before OT accrues
    public OtMethod OtMethod { get; set; } = OtMethod.CASH;
    public bool Temporary { get; set; }                       // probation / fixed-term

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
