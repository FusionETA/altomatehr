using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Overtime.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Overtime;

public class OvertimeRepository : IOvertimeRepository
{
    private readonly AppDbContext _db;

    public OvertimeRepository(AppDbContext db) => _db = db;

    public Task<List<OvertimeRequest>> GetAllAsync() =>
        _db.OvertimeRequests.OrderByDescending(r => r.WorkDate).ToListAsync();

    public Task<OvertimeRequest?> GetByIdAsync(string id) =>
        _db.OvertimeRequests.FirstOrDefaultAsync(r => r.Id == id);

    public Task<List<OvertimeRequest>> GetByEmployeeAsync(string employeeId) =>
        _db.OvertimeRequests
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.WorkDate)
            .ToListAsync();

    public Task<OvertimeRequest?> GetByPhotoUrlAsync(string photoUrl) =>
        _db.OvertimeRequests.FirstOrDefaultAsync(r =>
            r.BeforePhotoUrl == photoUrl || r.AfterPhotoUrl == photoUrl);

    public async Task<OvertimeRequest> AddAsync(OvertimeRequest request)
    {
        _db.OvertimeRequests.Add(request);
        await _db.SaveChangesAsync();
        return request;
    }

    public async Task UpdateAsync(OvertimeRequest request)
    {
        _db.OvertimeRequests.Update(request);
        await _db.SaveChangesAsync();
    }
}
