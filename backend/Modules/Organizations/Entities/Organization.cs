using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Organizations.Entities;

// The tenant. Every tenant-scoped entity (User, Claim, …) carries this org's Id.
// Organization itself is NOT tenant-scoped (it's the top of the tree).
public class Organization
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    // Org-level defaults that settings/modules read (expanded as we build settings).
    [MaxLength(3)]
    public string DefaultCurrency { get; set; } = "MYR";

    [Precision(10, 4)]
    public decimal DefaultMileageRate { get; set; }

    // How far (metres) from a project's geofence centre still counts as on-site.
    public int GeofenceRadiusMeters { get; set; } = 200;

    public DateTime CreatedAt { get; set; }
}
