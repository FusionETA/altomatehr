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

    // The request a before/after file belongs to — how photo access decides
    // who may open it. The single-photo columns hold only the FIRST file of
    // each side, so the lists are searched too: a substring match narrows it
    // in SQL, then the exact url is confirmed in memory (a url is never a
    // prefix of another's JSON by accident, but "contains" alone isn't proof).
    public async Task<OvertimeRequest?> GetByPhotoUrlAsync(string photoUrl)
    {
        var candidates = await _db.OvertimeRequests
            .Where(r => r.BeforePhotoUrl == photoUrl
                     || r.AfterPhotoUrl == photoUrl
                     || (r.BeforeAttachmentsJson != null && r.BeforeAttachmentsJson.Contains(photoUrl))
                     || (r.AfterAttachmentsJson != null && r.AfterAttachmentsJson.Contains(photoUrl)))
            .ToListAsync();

        return candidates.FirstOrDefault(r =>
            r.BeforeAttachments.Any(a => a.Url == photoUrl) || r.AfterAttachments.Any(a => a.Url == photoUrl));
    }

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
