using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Policies.Entities;

// A policy's own take on one of the company's CUSTOM overtime types: its own
// multiplier (null = the company's), or off for this policy's staff. No row =
// on, at the company's multiplier. The built-in kinds use the OtRate* columns
// on EmployeePolicy instead. Unique per (policy, type).
public class PolicyOvertimeRate : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string PolicyId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string OvertimeTypeId { get; set; } = string.Empty;

    [Precision(4, 2)] public decimal? Multiplier { get; set; }

    public bool Enabled { get; set; } = true;
}
