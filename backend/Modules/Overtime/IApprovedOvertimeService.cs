namespace AltomateHR.Api.Modules.Overtime;

// Approved overtime for a period, already split by the kind of day it was
// worked on — the question payroll asks when it pays OT, answered in one call.
//
// Its own narrow interface rather than another member on IOvertimeService so
// payroll depends on exactly this, and its tests stub one method instead of
// the whole submission/approval surface.
public interface IApprovedOvertimeService
{
    // Keyed by user id (the Overtime module works in users, like Leave).
    // Only APPROVED requests whose WorkDate falls in [from, to], both inclusive.
    // Employees with nothing approved are absent rather than zero. Pass
    // `employeeId` to ask about one person without classifying everyone's.
    Task<IReadOnlyDictionary<string, ApprovedOvertimeMinutes>> GetApprovedMinutesAsync(
        DateTime from, DateTime to, string? employeeId = null);
}

// Minutes rather than hours so nothing is rounded before payroll decides how.
public record ApprovedOvertimeMinutes(int NormalDayMin, int RestDayMin, int PublicHolidayMin)
{
    public int TotalMin => NormalDayMin + RestDayMin + PublicHolidayMin;
}
