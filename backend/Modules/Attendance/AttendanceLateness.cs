namespace AltomateHR.Api.Modules.Attendance;

// How late a clock-in was against the scheduled start.
//
// Pure arithmetic, kept out of AttendanceService so it can be tested without a
// database — same split as AttendanceHoursMath.
//
// The record has carried a LateByMin column since the module landed, but only
// the seeder ever filled it: a real clock-in left it null, so the UI's "Late
// 26m" badge only ever appeared on demo rows. Clocking in an hour after your
// shift started showed nothing at all.
public static class AttendanceLateness
{
    // Null means "no opinion" rather than "on time": there's no schedule to
    // measure against, or the clock-in was at/before the start. The UI shows a
    // badge only when there's a number, so null keeps a punctual day clean.
    //
    // `scheduledStart` is the shift's local "HH:mm". `timeInUtc` is the stored
    // instant; both are compared in the org's local timezone, since a shift
    // starting 09:00 in KL is 01:00 UTC and comparing across the two would
    // report every morning as eight hours late.
    public static int? Minutes(
        DateTime timeInUtc,
        string? scheduledStart,
        string timeZoneId = AttendanceTime.DefaultTimeZone)
    {
        if (!TryParseHm(scheduledStart, out var startMinutes)) return null;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(timeInUtc, DateTimeKind.Utc), tz);

        var actualMinutes = local.Hour * 60 + local.Minute;
        var late = actualMinutes - startMinutes;

        // Early and exactly-on-time both report null; only a positive overrun
        // is a fact worth showing.
        return late > 0 ? late : null;
    }

    private static bool TryParseHm(string? value, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], out var h)
            || !int.TryParse(parts[1], out var m)
            || h is < 0 or > 23
            || m is < 0 or > 59)
        {
            return false;
        }

        minutes = h * 60 + m;
        return true;
    }
}
