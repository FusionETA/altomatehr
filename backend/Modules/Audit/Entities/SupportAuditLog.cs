using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Audit.Entities;

// Fusioneta-side record of what support did inside a customer's org.
//
// In the customer's own audit log a support action is shown as "System
// (Support)" — which member of staff it was is Fusioneta's business, not the
// customer's. This row keeps the REAL actor, so Fusioneta can still answer
// "who did that". Deliberately NOT tenant-scoped: it spans every org, and no
// customer endpoint reads it.
public class SupportAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)] public string OrganizationId { get; set; } = string.Empty;
    [MaxLength(40)] public string SupportUserId { get; set; } = string.Empty;
    [MaxLength(160)] public string SupportEmail { get; set; } = string.Empty;

    [MaxLength(80)] public string Action { get; set; } = string.Empty;
    [MaxLength(500)] public string Summary { get; set; } = string.Empty;
    [MaxLength(60)] public string? TargetType { get; set; }
    [MaxLength(64)] public string? TargetId { get; set; }
    [MaxLength(64)] public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }
}
