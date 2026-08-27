using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Shifts.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Shifts;

public class ShiftRepository : IShiftRepository
{
    private readonly AppDbContext _db;

    public ShiftRepository(AppDbContext db) => _db = db;

    // All queries are auto-scoped to the current org by the global query filter.
    public Task<List<Shift>> GetAllAsync() =>
        _db.Shifts.OrderBy(s => s.Name).ToListAsync();

    public Task<List<Shift>> GetForProjectAsync(string projectId) =>
        _db.Shifts.Where(s => s.ProjectId == projectId).OrderBy(s => s.Name).ToListAsync();

    public Task<Shift?> GetByIdAsync(string id) =>
        _db.Shifts.FirstOrDefaultAsync(s => s.Id == id);

    public Task<Shift?> GetByNameAsync(string projectId, string name) =>
        _db.Shifts.FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Name == name);

    public Task<Shift?> GetDefaultForProjectAsync(string projectId) =>
        _db.Shifts.FirstOrDefaultAsync(s => s.ProjectId == projectId && s.IsDefault);

    public async Task<Shift> AddAsync(Shift shift)
    {
        _db.Shifts.Add(shift);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return shift;
    }

    public async Task UpdateAsync(Shift shift)
    {
        _db.Shifts.Update(shift);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Shift shift)
    {
        _db.Shifts.Remove(shift);
        await _db.SaveChangesAsync();
    }

    public async Task ClearDefaultForProjectExceptAsync(string projectId, string keepId)
    {
        var others = await _db.Shifts
            .Where(s => s.ProjectId == projectId && s.IsDefault && s.Id != keepId)
            .ToListAsync();
        foreach (var s in others) s.IsDefault = false;
        if (others.Count > 0) await _db.SaveChangesAsync();
    }
}
