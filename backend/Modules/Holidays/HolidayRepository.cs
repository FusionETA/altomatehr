using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Holidays.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Holidays;

public class HolidayRepository : IHolidayRepository
{
    private readonly AppDbContext _db;

    public HolidayRepository(AppDbContext db) => _db = db;

    // All queries are auto-scoped to the current org by the global query filter.
    public Task<List<Holiday>> GetAllAsync() =>
        _db.Holidays.OrderBy(h => h.Date).ToListAsync();

    public Task<List<Holiday>> GetInRangeAsync(DateTime from, DateTime to) =>
        _db.Holidays
            .Where(h => h.Date >= from && h.Date <= to)
            .OrderBy(h => h.Date)
            .ToListAsync();

    public Task<Holiday?> GetByIdAsync(string id) =>
        _db.Holidays.FirstOrDefaultAsync(h => h.Id == id);

    public Task<Holiday?> GetByDateAndScopeAsync(DateTime date, string? projectId) =>
        _db.Holidays.FirstOrDefaultAsync(h => h.Date == date && h.ProjectId == projectId);

    public Task<List<Holiday>> GetForDateAsync(DateTime date, string? projectId) =>
        _db.Holidays
            .Where(h => h.Date == date && (h.ProjectId == null || h.ProjectId == projectId))
            .ToListAsync();

    public async Task<Holiday> AddAsync(Holiday holiday)
    {
        _db.Holidays.Add(holiday);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return holiday;
    }

    public async Task UpdateAsync(Holiday holiday)
    {
        _db.Holidays.Update(holiday);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Holiday holiday)
    {
        _db.Holidays.Remove(holiday);
        await _db.SaveChangesAsync();
    }
}
