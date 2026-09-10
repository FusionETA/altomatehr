using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Projects.Entities;

// A project within an org. Shared entity — claims are filed against a project, and
// attendance/leave will reference it too. Soft-archived (not hard-deleted).
public class Project : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string? XeroProjectId { get; set; }

    [MaxLength(40)]
    public string? XeroStatus { get; set; }

    public DateTime? XeroSyncedAt { get; set; }

    // Street address of the site, free text. Separate from Name because the
    // name is the label everything else refers to (claims, attendance, the
    // project picker), and folding an address into it would push a postcode
    // into every one of those. Null for a project that has no fixed site.
    [MaxLength(400)]
    public string? Location { get; set; }

    // Geofence centre. Both null → the project isn't geofenced (attendance
    // clock-ins against it skip the distance check).
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    // Comma-separated IP addresses allowed to clock in/out against this project.
    // Consulted only for employees whose policy has RequireIpWhitelist on; null
    // or empty means the allowlist check is silently skipped for this project.
    [MaxLength(1000)]
    public string? AllowedIps { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }
}
