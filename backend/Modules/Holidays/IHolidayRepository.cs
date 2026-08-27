using AltomateHR.Api.Modules.Holidays.Entities;

namespace AltomateHR.Api.Modules.Holidays;

public interface IHolidayRepository
{
    Task<List<Holiday>> GetAllAsync();
    Task<List<Holiday>> GetInRangeAsync(DateTime from, DateTime to);
    Task<Holiday?> GetByIdAsync(string id);

    // Same scope + same date = duplicate. Org-wide and project-specific rows
    // for the same date are allowed to coexist (the project row is an addition,
    // not an override).
    Task<Holiday?> GetByDateAndScopeAsync(DateTime date, string? projectId);

    // Every holiday observed on `date` by either the whole org or `projectId`.
    Task<List<Holiday>> GetForDateAsync(DateTime date, string? projectId);

    Task<Holiday> AddAsync(Holiday holiday);
    Task UpdateAsync(Holiday holiday);
    Task DeleteAsync(Holiday holiday);
}
