namespace AltomateHR.Api.Modules.Overtime;

public interface IOtRateService
{
    // Which OT multiplier applies to this employee on this date, and why.
    Task<OtRateResolution> ResolveAsync(string employeeId, DateTime date, string? projectId);
}

// What kind of day this is, for OT-rate purposes. Public holiday beats rest
// day when a holiday falls on one — the higher premium wins, matching the
// reference app.
public enum OtDayType
{
    NORMAL_DAY,
    REST_DAY,
    PUBLIC_HOLIDAY,
}

// Multiplier is null when OT doesn't apply at all (policy has OtEnabled off,
// or OtMethod is TIME_BANK which banks 1:1 rather than paying a premium) —
// Reason says which.
//
// OutOfShiftMultiplier is the x-HRP rate for hours beyond the regular shift;
// InShiftMultiplier is the x-ORP premium for hours worked WITHIN the regular
// shift on a rest day / public holiday (always null on a normal day, where
// in-shift hours are just ordinary pay).
public record OtRateResolution(
    OtDayType DayType,
    decimal? OutOfShiftMultiplier,
    decimal? InShiftMultiplier,
    string Reason);
