using AltomateHR.Api.Modules.Holidays.Dtos;

namespace AltomateHR.Api.Modules.Holidays;

public interface IHolidayService
{
    Task<IEnumerable<HolidayDto>> GetAllAsync();
    Task<IEnumerable<HolidayDto>> GetInRangeAsync(DateTime from, DateTime to);
    Task<HolidaySaveResult> CreateAsync(SaveHolidayDto dto);
    Task<HolidaySaveResult> UpdateAsync(string id, SaveHolidayDto dto);
    Task<bool> DeleteAsync(string id);

    // Whether `date` is observed as a public holiday, either org-wide or by
    // `projectId`. Used by the OT rate resolver.
    Task<bool> IsHolidayAsync(DateTime date, string? projectId);

    // Load a country's public holidays for one year from a public calendar API.
    //
    // Org-wide only. A project that observes a different state's calendar is
    // still added by hand — importing per project would need a per-project
    // country, which nothing models.
    Task<HolidayImportResult> ImportAsync(int year, string countryCode);
}

// Skipped counts dates the org already had. Re-running an import is a normal
// thing to do — a calendar gets revised — so an existing date is left ALONE
// rather than overwritten: an admin may have renamed it, or moved the
// observed day, and silently undoing that is worse than importing nothing.
public record HolidayImportResult(
    bool Ok, int Imported, int Skipped, string? Source, string? Error = null);

// Ok=false, Error=null → 404. Ok=false, Error!=null → 400.
public record HolidaySaveResult(bool Ok, HolidayDto? Holiday, string? Error = null);
