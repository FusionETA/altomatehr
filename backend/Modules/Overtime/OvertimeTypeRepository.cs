using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Overtime.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Overtime;

public class OvertimeTypeRepository : IOvertimeTypeRepository
{
    private readonly AppDbContext _db;

    public OvertimeTypeRepository(AppDbContext db) => _db = db;

    public Task<List<OvertimeType>> GetAllAsync() =>
        _db.OvertimeTypes.ToListAsync();

    public Task<OvertimeType?> GetByIdAsync(string id) => _db.OvertimeTypes.FirstOrDefaultAsync(t => t.Id == id);

    public async Task AddRangeAsync(IEnumerable<OvertimeType> types)
    {
        _db.OvertimeTypes.AddRange(types);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(OvertimeType type)
    {
        _db.OvertimeTypes.Update(type);
        await _db.SaveChangesAsync();
    }
}
