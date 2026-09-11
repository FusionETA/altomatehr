using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class PortalCredentialRepository : IPortalCredentialRepository
{
    private readonly AppDbContext _db;

    public PortalCredentialRepository(AppDbContext db) => _db = db;

    public Task<List<PayrollPortalCredential>> GetAllAsync() =>
        _db.PayrollPortalCredentials.OrderBy(c => c.Portal).ToListAsync();

    public Task<PayrollPortalCredential?> GetAsync(PortalKind portal) =>
        _db.PayrollPortalCredentials.FirstOrDefaultAsync(c => c.Portal == portal);

    public async Task<PayrollPortalCredential> AddAsync(PayrollPortalCredential credential)
    {
        _db.PayrollPortalCredentials.Add(credential);
        await _db.SaveChangesAsync();
        return credential;
    }

    public async Task UpdateAsync(PayrollPortalCredential credential)
    {
        _db.PayrollPortalCredentials.Update(credential);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(PayrollPortalCredential credential)
    {
        _db.PayrollPortalCredentials.Remove(credential);
        await _db.SaveChangesAsync();
    }
}
