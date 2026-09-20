using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Projects.Entities;

// One geofenced site on a project — an office, a gate, a site entrance. A
// project may have several: 89 of the 138 geofenced projects in the legacy
// data have more than one, and a single centre per project would have dropped
// 112 of its 250 points.
//
// Project.Latitude/Longitude stay as the fallback for a project that has no
// points, exactly as the legacy schema kept its scalar pair.
//
// SortOrder is part of the data, not decoration: the check walks the points in
// order and the FIRST one inside the radius wins, so reordering them can change
// which site an employee is judged against. The legacy system ordered by
// createdAt ascending; that order is carried across into this column so v2
// doesn't depend on timestamp ties or storage order to reproduce it.
public class ProjectGeofencePoint : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string ProjectId { get; set; } = string.Empty;

    // What the site is called, shown to the employee when they are off-site so
    // "2.7 km away" says 2.7 km from WHAT.
    [MaxLength(160)]
    public string Label { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
