using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Modules.Policies.Dtos;

public class PolicyLeaveEntitlementDto
{
    public string LeaveTypeId { get; set; } = string.Empty;
    public double DefaultDays { get; set; }
}

public class PolicyDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
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
    public SalaryType SalaryType { get; set; }
    public bool OtEnabled { get; set; }
    public int OtDailyThresholdMinutes { get; set; }
    public OtMethod OtMethod { get; set; }
    public decimal OtRateNormalDay { get; set; }
    public decimal OtRatePublicHoliday { get; set; }
    public decimal OtRateRestDay { get; set; }
    public decimal OtRatePublicHolidayInShift { get; set; }
    public decimal OtRateRestDayInShift { get; set; }
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
    public SalaryType SalaryType { get; set; } = SalaryType.HOURLY;
    public bool OtEnabled { get; set; } = true;

    [Range(0, 1440)]
    public int OtDailyThresholdMinutes { get; set; } = 480;

    public OtMethod OtMethod { get; set; } = OtMethod.CASH;

    [Range(0, 99.99)] public decimal OtRateNormalDay { get; set; } = 1.50m;
    [Range(0, 99.99)] public decimal OtRatePublicHoliday { get; set; } = 3.00m;
    [Range(0, 99.99)] public decimal OtRateRestDay { get; set; } = 2.00m;
    [Range(0, 99.99)] public decimal OtRatePublicHolidayInShift { get; set; } = 2.00m;
    [Range(0, 99.99)] public decimal OtRateRestDayInShift { get; set; } = 1.00m;
    [Range(0, 99999999.99)] public decimal? OtSalaryThreshold { get; set; }
    public bool Temporary { get; set; }

    public List<PolicyLeaveEntitlementDto> LeaveEntitlements { get; set; } = [];
}
