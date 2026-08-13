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

    // Geofence centre. Both null → the project isn't geofenced (attendance
    // clock-ins against it skip the distance check).
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; }
}
