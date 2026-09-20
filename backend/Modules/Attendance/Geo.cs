namespace AltomateHR.Api.Modules.Attendance;

// Pure geo math, ported from the monolith's lib/geo.ts. Haversine great-circle
// distance in metres + a geofence check (project centre + org radius). No deps.
public static class Geo
{
    public const int DefaultRadiusMeters = 200;

    public static double HaversineMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double earthRadius = 6_371_000; // metres
        static double ToRad(double deg) => deg * Math.PI / 180;

        var dLat = ToRad(lat2 - lat1);
        var dLng = ToRad(lng2 - lng1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * earthRadius * Math.Asin(Math.Sqrt(a));
    }

    // The outcome of checking coords against a project's sites. `Distance` is
    // the MATCHED site's distance when inside, and the NEAREST site's when not —
    // the two differ, and the off-site figure is the one an employee is shown.
    public readonly record struct MultiResult(bool Inside, double? Distance, string? Label);

    // Ported from the monolith's checkGeofenceMulti. Walks the sites IN ORDER and
    // stops at the first one inside the radius; that ordering is data (see
    // ProjectGeofencePoint.SortOrder), so the caller must not re-sort. When none
    // match, reports the nearest — not the last one tried, and not the first.
    //
    // No sites, or no coords, is not "inside": it is unknown, and the caller
    // decides what that means (see AttendanceService.EvaluateGeofenceAsync).
    public static MultiResult CheckSites(
        double? lat, double? lng,
        IReadOnlyList<(string Label, double Latitude, double Longitude)> sites,
        int radiusMeters)
    {
        if (lat is null || lng is null || sites.Count == 0) return new(false, null, null);

        MultiResult nearest = new(false, null, null);
        foreach (var site in sites)
        {
            var distance = HaversineMeters(lat.Value, lng.Value, site.Latitude, site.Longitude);
            if (distance <= radiusMeters) return new(true, distance, site.Label);
            if (nearest.Distance is null || distance < nearest.Distance)
                nearest = new(false, distance, site.Label);
        }
        return nearest;
    }

    // Distance from the employee's coords to a project's geofence centre, or null
    // when either the employee coords or the project centre is missing.
    public static double? DistanceToProject(double? empLat, double? empLng, double? projLat, double? projLng)
    {
        if (empLat is null || empLng is null || projLat is null || projLng is null) return null;
        return HaversineMeters(empLat.Value, empLng.Value, projLat.Value, projLng.Value);
    }
}
