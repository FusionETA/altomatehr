using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Projects.Entities;

// One entry in a project's IP allowlist: an office network an employee may
// clock in from.
//
// A row rather than an entry in Project.AllowedIps, which is a comma-separated
// string and therefore cannot carry a label. "203.0.113.0/24" tells whoever
// reads the settings screen nothing; "KL office wifi" tells them whether it is
// safe to delete. The legacy system reached the same conclusion and replaced
// its own comma-separated column with a labelled list.
//
// Project.AllowedIps stays as the fallback for a project that has no rows, the
// same arrangement as the geofence points and the scalar lat/lng.
public class ProjectAllowedIp : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string ProjectId { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Label { get; set; } = string.Empty;

    // A bare IPv4 address or an IPv4 CIDR range. Stored as typed, not
    // normalised: the admin should see back what they entered. Canonicalising
    // happens at match time.
    [MaxLength(64)]
    public string Cidr { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
