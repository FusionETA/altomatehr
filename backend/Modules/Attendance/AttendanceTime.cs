namespace AltomateHR.Api.Modules.Attendance;

// Attendance is bucketed by the org's local business day. For now the timezone
// is a constant (Asia/Kuala_Lumpur) — a per-org timezone arrives with the
// attendance-settings slice. Mirrors the monolith's `startOfLocalDay`: the day
// key is the UTC-midnight instant of the local calendar date, so an early
// shift (e.g. 06:43 in KL, still "yesterday" in UTC) is filed under the correct
// local day rather than rolling over at 08:00 local.
public static class AttendanceTime
{
    public const string DefaultTimeZone = "Asia/Kuala_Lumpur";

    public static DateTime StartOfLocalDay(DateTime utcNow, string timeZoneId = DefaultTimeZone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);

        // The local calendar date, stored at UTC midnight as a pure day key.
        return new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Utc);
    }
}
