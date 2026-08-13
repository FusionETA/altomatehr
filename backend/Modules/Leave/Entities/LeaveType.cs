using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Leave.Entities;

// An org-defined kind of leave (Annual, Medical, Unpaid…). Employees apply
// against a type; the yearly balance is DefaultDays − approved days taken.
// Accrual, carry-forward and per-policy entitlements are deferred.
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

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
