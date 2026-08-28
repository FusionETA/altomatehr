using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Organizations.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Organizations;

// Auto-scoped to the current org by the global query filter.
public class OrgHolidayRepository : IOrgHolidayRepository
{
    private readonly AppDbContext _db;

    public OrgHolidayRepository(AppDbContext db) => _db = db;

    public Task<List<OrgHoliday>> GetAllAsync() =>
        _db.OrgHolidays.OrderBy(h => h.Date).ToListAsync();

    public Task<List<OrgHoliday>> GetByYearAsync(int year) =>
        _db.OrgHolidays.Where(h => h.Date.Year == year).OrderBy(h => h.Date).ToListAsync();

    // Whole-year replace: the admin edits a calendar and PUTs the result, so a
    // removed date must actually disappear. Scoped to the year so editing 2026
    // never touches 2025.
    public async Task ReplaceYearAsync(int year, IEnumerable<OrgHoliday> holidays)
    {
        var existing = await _db.OrgHolidays.Where(h => h.Date.Year == year).ToListAsync();
        _db.OrgHolidays.RemoveRange(existing);
        await _db.OrgHolidays.AddRangeAsync(holidays);
        await _db.SaveChangesAsync();
    }
}
