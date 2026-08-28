using AltomateHR.Api.Modules.Organizations.Entities;

namespace AltomateHR.Api.Modules.Organizations;

public interface IOrgHolidayRepository
{
    Task<List<OrgHoliday>> GetAllAsync();
    Task<List<OrgHoliday>> GetByYearAsync(int year);
    Task ReplaceYearAsync(int year, IEnumerable<OrgHoliday> holidays);
}
