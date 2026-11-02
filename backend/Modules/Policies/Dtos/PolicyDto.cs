using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Policies.Dtos;

public class PolicyLeaveEntitlementDto
{
    public string LeaveTypeId { get; set; } = string.Empty;
    public double DefaultDays { get; set; }
}

// The policy's take on one of the company's own overtime types: its own
// multiplier (null = the company's), or off for this policy's staff.
public class PolicyOvertimeRateDto
{
    [Required]
    public string OvertimeTypeId { get; set; } = string.Empty;

    [Range(0, 99.99)]
    public decimal? Multiplier { get; set; }

    public bool Enabled { get; set; } = true;
}

public class PolicyDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
    // Whether the CALLER may edit it (GET /policies). False for an admin
    // limited to some policies when this one also covers the company's
    // admins or owner — that stays the Owner's.
    public bool CanEdit { get; set; } = true;
    public bool CanAccessAttendance { get; set; }
    public bool CanAccessClaims { get; set; }
    public bool CanAccessLeave { get; set; }
    public bool RequireGeofence { get; set; }
    public bool RequireSelfie { get; set; }
    public bool RequireClockOutSelfie { get; set; }
    public bool CaptureLocationOnBreakStart { get; set; }
    public bool CaptureLocationOnBreakEnd { get; set; }
    public bool RequireIpWhitelist { get; set; }
    public bool GeolocationEnabled { get; set; }
    public bool CaptureLocationOnClockIn { get; set; }
    public bool CaptureLocationOnClockOut { get; set; }
    public bool AutoClockOutEnabled { get; set; }
    public int? AutoClockOutAfterMinutes { get; set; }
    public SalaryType SalaryType { get; set; }
    public bool OtEnabled { get; set; }
    public int OtDailyThresholdMinutes { get; set; }
    public OtMethod OtMethod { get; set; }
    // Null = the company's multiplier for that built-in overtime type.
    public decimal? OtRateNormalDay { get; set; }
    public decimal? OtRatePublicHoliday { get; set; }
    public decimal? OtRateRestDay { get; set; }

    // Only the company's own types this policy changes; any other is on, at
    // the company's multiplier.
    public List<PolicyOvertimeRateDto> OvertimeRates { get; set; } = [];
    public decimal? OtSalaryThreshold { get; set; }
    public bool Temporary { get; set; }
    public List<PolicyLeaveEntitlementDto> LeaveEntitlements { get; set; } = [];
}

// Create/update a policy. Id / IsArchived / org are server-controlled.
// LeaveEntitlements, when provided, replace the policy's entitlement set.
public class SavePolicyDto
{
    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(400)]
    public string? Description { get; set; }

    public bool CanAccessAttendance { get; set; } = true;
    public bool CanAccessClaims { get; set; } = true;
    public bool CanAccessLeave { get; set; } = true;
    public bool RequireGeofence { get; set; } = true;
    public bool RequireSelfie { get; set; }
    public bool RequireClockOutSelfie { get; set; }
    public bool CaptureLocationOnBreakStart { get; set; } = true;
    public bool CaptureLocationOnBreakEnd { get; set; } = true;
    public bool RequireIpWhitelist { get; set; }
    public bool GeolocationEnabled { get; set; } = true;
    public bool CaptureLocationOnClockIn { get; set; } = true;
    public bool CaptureLocationOnClockOut { get; set; } = true;
    public bool AutoClockOutEnabled { get; set; }

    [Range(1, 1440)]
    public int? AutoClockOutAfterMinutes { get; set; }

    public SalaryType SalaryType { get; set; } = SalaryType.HOURLY;
    public bool OtEnabled { get; set; } = true;

    [Range(0, 1440)]
    public int OtDailyThresholdMinutes { get; set; } = 480;

    public OtMethod OtMethod { get; set; } = OtMethod.CASH;

    // Null = the company's multiplier (System Settings → Overtime types).
    [Range(0, 99.99)] public decimal? OtRateNormalDay { get; set; }
    [Range(0, 99.99)] public decimal? OtRatePublicHoliday { get; set; }
    [Range(0, 99.99)] public decimal? OtRateRestDay { get; set; }

    // The company's own overtime types: null = leave this policy's as they
    // are; a list replaces them (a type left out is on, at the company rate).
    public List<PolicyOvertimeRateDto>? OvertimeRates { get; set; }
    [Range(0, 99999999.99)] public decimal? OtSalaryThreshold { get; set; }
    public bool Temporary { get; set; }

    public List<PolicyLeaveEntitlementDto> LeaveEntitlements { get; set; } = [];
}
