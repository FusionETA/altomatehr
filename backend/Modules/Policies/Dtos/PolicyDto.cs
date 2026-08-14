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
    public SalaryType SalaryType { get; set; }
    public bool OtEnabled { get; set; }
    public int OtDailyThresholdMinutes { get; set; }
    public OtMethod OtMethod { get; set; }
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
    public SalaryType SalaryType { get; set; } = SalaryType.HOURLY;
    public bool OtEnabled { get; set; } = true;

    [Range(0, 1440)]
    public int OtDailyThresholdMinutes { get; set; } = 480;

    public OtMethod OtMethod { get; set; } = OtMethod.CASH;
    public bool Temporary { get; set; }

    public List<PolicyLeaveEntitlementDto> LeaveEntitlements { get; set; } = [];
}
