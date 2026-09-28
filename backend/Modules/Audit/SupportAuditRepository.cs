using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Audit.Entities;

namespace AltomateHR.Api.Modules.Audit;

public class SupportAuditRepository : ISupportAuditRepository
{
    private readonly AppDbContext _db;

    public SupportAuditRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(SupportAuditLog entry)
    {
        _db.SupportAuditLogs.Add(entry);
        await _db.SaveChangesAsync();
    }
}
