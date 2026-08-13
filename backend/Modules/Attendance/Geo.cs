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

    // Distance from the employee's coords to a project's geofence centre, or null
    // when either the employee coords or the project centre is missing.
    public static double? DistanceToProject(double? empLat, double? empLng, double? projLat, double? projLng)
    {
        if (empLat is null || empLng is null || projLat is null || projLng is null) return null;
        return HaversineMeters(empLat.Value, empLng.Value, projLat.Value, projLng.Value);
    }
}
