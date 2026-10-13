using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Organizations.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Organizations;

// All queries — reads and the ExecuteUpdate writes alike — are auto-scoped to
// the current org by the global query filter.
//
// On MySQL each write is one UPDATE … WHERE statement (ExecuteUpdateAsync),
// atomic under InnoDB's row locks. The EF in-memory provider the tests use
// can't run ExecuteUpdate, so there the same WHERE is applied to tracked rows
// and only the changed columns are saved — the same result, minus the
// cross-request atomicity an in-memory test can't exercise anyway.
public class OrganizationSignatureRepository : IOrganizationSignatureRepository
{
    private readonly AppDbContext _db;

    public OrganizationSignatureRepository(AppDbContext db) => _db = db;

    private IQueryable<OrganizationSignature> Active =>
        _db.OrganizationSignatures.Where(s => s.RemovedAt == null);

    public Task<List<OrganizationSignature>> GetActiveAsync() =>
        Active.AsNoTracking()
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name)
            .ThenBy(s => s.CreatedAt)
            .ToListAsync();

    public Task<OrganizationSignature?> GetActiveByIdAsync(string id) =>
        Active.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

    public async Task AddAsync(OrganizationSignature signature)
    {
        _db.OrganizationSignatures.Add(signature);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        _db.Entry(signature).State = EntityState.Detached;   // later writes are statements, not tracked saves
    }

    public async Task<bool> MakeDefaultAsync(string id)
    {
        // Rows that need changing: the current default(s) and the target.
        var rows = Active.Where(s => s.IsDefault || s.Id == id);

        if (_db.Database.IsRelational())
        {
            var affected = await rows.ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDefault, s => s.Id == id));
            return affected > 0 && await Active.AnyAsync(s => s.Id == id && s.IsDefault);
        }

        var tracked = await rows.ToListAsync();
        foreach (var s in tracked) s.IsDefault = s.Id == id;
        await SaveAndDetachAsync(tracked);
        return tracked.Any(s => s.Id == id);
    }

    public async Task<bool> ClearDefaultAsync(string id)
    {
        var row = Active.Where(s => s.Id == id);

        if (_db.Database.IsRelational())
            return await row.ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDefault, false)) > 0;

        var tracked = await row.ToListAsync();
        foreach (var s in tracked) s.IsDefault = false;
        await SaveAndDetachAsync(tracked);
        return tracked.Count > 0;
    }

    public async Task<bool> UpdateDetailsAsync(string id, string name, string position)
    {
        var row = Active.Where(s => s.Id == id);

        if (_db.Database.IsRelational())
            return await row.ExecuteUpdateAsync(set => set
                .SetProperty(s => s.Name, name)
                .SetProperty(s => s.Position, position)) > 0;

        var tracked = await row.ToListAsync();
        foreach (var s in tracked)
        {
            s.Name = name;
            s.Position = position;
        }
        await SaveAndDetachAsync(tracked);
        return tracked.Count > 0;
    }

    public async Task<bool> SoftRemoveAsync(string id, DateTime removedAt)
    {
        var row = Active.Where(s => s.Id == id);

        if (_db.Database.IsRelational())
            return await row.ExecuteUpdateAsync(set => set
                .SetProperty(s => s.RemovedAt, removedAt)
                .SetProperty(s => s.IsDefault, false)) > 0;

        var tracked = await row.ToListAsync();
        foreach (var s in tracked)
        {
            s.RemovedAt = removedAt;
            s.IsDefault = false;
        }
        await SaveAndDetachAsync(tracked);
        return tracked.Count > 0;
    }

    // Tracked rows save only the properties that changed.
    private async Task SaveAndDetachAsync(List<OrganizationSignature> rows)
    {
        await _db.SaveChangesAsync();
        foreach (var s in rows) _db.Entry(s).State = EntityState.Detached;
    }
}
