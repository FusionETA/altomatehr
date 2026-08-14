using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Policies.Entities;

// Per-policy override of a leave type's yearly entitlement. When present, it
// wins over LeaveType.DefaultDays for employees on that policy; when absent,
// the type's default applies. Unique per (policy, leave type).
public class PolicyLeaveEntitlement : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string PolicyId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string LeaveTypeId { get; set; } = string.Empty;

    public double DefaultDays { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
