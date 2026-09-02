using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Leave.Entities;

// An org-defined kind of leave (Annual, Medical, Unpaid…). Employees apply
// against a type. The yearly balance comes from the employee's LeaveEntitlement
// row; the fields here are the org-wide CONFIG those rows are created from.
public class LeaveType : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(20)]
    public string Code { get; set; } = string.Empty;             // unique per org (e.g. "AL", "MC")

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    public bool Paid { get; set; } = true;

    // Annual entitlement in days. Typically 0 for unpaid types.
    public double DefaultDays { get; set; }

    // --- Accrual + carry-forward config ---

    // Org default; overridable per policy and per employee. LUMP_SUM means the
    // whole entitlement is available immediately, which is the common case.
    public LeaveAccrualMethod AccrualMethod { get; set; } = LeaveAccrualMethod.LUMP_SUM;

    // When true, unused days roll into next year at rollover.
    public bool CarryForward { get; set; }

    // Month (1-12) the carried days lapse. Required when CarryForward is true.
    public int? CarryExpiryMonth { get; set; }

    // Ceiling on how many days may carry. Null = uncapped.
    public double? MaxCarryForwardDays { get; set; }

    // When true AND AccrualMethod is LUMP_SUM, a mid-year hire gets a prorated
    // slice of DefaultDays in their hire year only.
    public bool ProrateFirstYear { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
