using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Organizations.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Organizations;

public class OrganizationSignatureRepository : IOrganizationSignatureRepository
{
    private readonly AppDbContext _db;

    public OrganizationSignatureRepository(AppDbContext db) => _db = db;

    // All queries are auto-scoped to the current org by the global query filter.
    public Task<List<OrganizationSignature>> GetActiveAsync() =>
        _db.OrganizationSignatures
            .Where(s => s.RemovedAt == null)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync();

    public Task<OrganizationSignature?> GetActiveByIdAsync(string id) =>
        _db.OrganizationSignatures.FirstOrDefaultAsync(s => s.Id == id && s.RemovedAt == null);

    public async Task AddAsync(OrganizationSignature signature, params OrganizationSignature[] alsoUpdate)
    {
        _db.OrganizationSignatures.Add(signature);
        if (alsoUpdate.Length > 0) _db.OrganizationSignatures.UpdateRange(alsoUpdate);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
    }

    public async Task UpdateAsync(params OrganizationSignature[] signatures)
    {
        _db.OrganizationSignatures.UpdateRange(signatures);
        await _db.SaveChangesAsync();
    }
}
