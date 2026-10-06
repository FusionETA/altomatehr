namespace AltomateHR.Api.Modules.Attendance.Dtos;

// Where the caller stands against a project's geofence right now, for the
// clock card's "On site · 45 m away" line. Worked out by the same evaluation
// the clock-in itself runs, so the line can never say on-site to someone the
// clock-in will then ask for a reason and photo.
public class GeofenceCheckDto
{
    // False when the project has no geofenced site: there is nothing to be
    // inside or outside of, and no distance.
    public bool Geofenced { get; set; }

    // To the matched site when inside, the nearest site when not. Null
    // without a location.
    public double? DistanceMeters { get; set; }

    public bool WithinRadius { get; set; }

    // The organization's radius — shown as the limit when off-site.
    public int RadiusMeters { get; set; }

    // Whether the employee's policy enforces the geofence. When it doesn't,
    // being outside asks for nothing, so the card only states the distance.
    public bool Enforced { get; set; }
}
