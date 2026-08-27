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
}

// Ok=false, Error=null → 404. Ok=false, Error!=null → 400.
public record HolidaySaveResult(bool Ok, HolidayDto? Holiday, string? Error = null);
