using AltomateHR.Api.Modules.Organizations.Dtos;

namespace AltomateHR.Api.Modules.Organizations;

public interface IOrgHolidayService
{
    Task<IEnumerable<OrgHolidayDto>> GetAsync(int? year);
    Task<HolidaySaveResult> ReplaceYearAsync(int year, SaveHolidaysDto dto);

    // Dates in `year`, for day-counting. Kept separate from GetAsync so callers
    // that only need the set don't pay for DTO mapping.
    Task<IReadOnlySet<DateTime>> GetDatesAsync(int year);
}

public record HolidaySaveResult(bool Ok, IEnumerable<OrgHolidayDto>? Holidays, string? Error);
