using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Leave.Entities;

namespace AltomateHR.Api.Modules.Leave.Dtos;

public class LeaveTypeDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Paid { get; set; }
    public double DefaultDays { get; set; }
    public bool IsArchived { get; set; }
    public LeaveAccrualMethod AccrualMethod { get; set; }
    public bool CarryForward { get; set; }
    public int? CarryExpiryMonth { get; set; }
    public double? MaxCarryForwardDays { get; set; }
    public bool ProrateFirstYear { get; set; }
}

// Used for both create and update. (Id / IsArchived / org are server-controlled.)
public class SaveLeaveTypeDto
{
    [Required, MaxLength(20)]
    public string Code { get; set; } = string.Empty;

    [Required, MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public bool Paid { get; set; } = true;

    [Range(0, 365)]
    public double DefaultDays { get; set; }

    // --- Accrual + carry-forward config ---
    // Both are restricted to the ANNUAL code by LeaveTypeService, matching
    // production: only annual leave accrues monthly or carries forward.
    public LeaveAccrualMethod AccrualMethod { get; set; } = LeaveAccrualMethod.LUMP_SUM;

    public bool CarryForward { get; set; }

    // Required (1-12) when CarryForward is true.
    public int? CarryExpiryMonth { get; set; }

    public double? MaxCarryForwardDays { get; set; }

    public bool ProrateFirstYear { get; set; }
}
