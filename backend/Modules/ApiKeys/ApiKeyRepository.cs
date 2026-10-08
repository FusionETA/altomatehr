using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.ApiKeys;

public class ApiKeyRepository : IApiKeyRepository
{
    private readonly AppDbContext _db;

    public ApiKeyRepository(AppDbContext db) => _db = db;

    // The auth handler calls this BEFORE any org is on the request (no "current org"
    // yet), and a key isn't found by its own org anyway — so IgnoreQueryFilters. The
    // matched key then TELLS us which org the request belongs to.
    // A key whose company no longer exists is treated as unknown. Deleting a
    // company removes its keys today, but an integration (ABPay) decides a
    // company is gone by its key being rejected — so a key left behind must
    // never still authenticate. One query: the company check is an EXISTS.
    public Task<ApiKey?> GetByHashAsync(string tokenHash) =>
        _db.ApiKeys.IgnoreQueryFilters().FirstOrDefaultAsync(k =>
            k.TokenHash == tokenHash
            && _db.Organizations.Any(o => o.Id == k.OrganizationId));

    // Management reads run under a JWT Owner, so the tenant filter scopes these to the
    // Owner's active org automatically.
    public Task<List<ApiKey>> GetForCurrentOrgAsync() =>
        _db.ApiKeys.OrderByDescending(k => k.CreatedAt).ToListAsync();

    public Task<ApiKey?> GetByIdForCurrentOrgAsync(string id) =>
        _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id);

    // A handful of rows per org, so the names come back and the caller matches
    // them in code. The tenant filter keeps it to the current org.
    public Task<List<string>> GetActiveNamesForCurrentOrgAsync() =>
        _db.ApiKeys.Where(k => k.Active).Select(k => k.Name).ToListAsync();

    public async Task AddAsync(ApiKey key)
    {
        key.CreatedAt = key.CreatedAt == default ? DateTime.UtcNow : key.CreatedAt;
        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(ApiKey key)
    {
        _db.ApiKeys.Update(key);
        await _db.SaveChangesAsync();
    }

    public async Task RecordUsageAsync(ApiKeyAuditLog log)
    {
        log.CreatedAt = log.CreatedAt == default ? DateTime.UtcNow : log.CreatedAt;
        _db.ApiKeyAuditLogs.Add(log);

        // Bump LastUsedAt on the same key. IgnoreQueryFilters so this always targets the
        // row by id, independent of the request's current-org filter.
        var key = await _db.ApiKeys.IgnoreQueryFilters().FirstOrDefaultAsync(k => k.Id == log.ApiKeyId);
        if (key is not null) key.LastUsedAt = log.CreatedAt;

        await _db.SaveChangesAsync();
    }
}
