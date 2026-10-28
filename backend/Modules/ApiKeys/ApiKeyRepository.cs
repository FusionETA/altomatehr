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
    // FAH keys are left out: they belong to one person's assistant session, not
    // to the integrations an Owner manages.
    public Task<List<ApiKey>> GetForCurrentOrgAsync() =>
        _db.ApiKeys.Where(k => k.ActingUserId == null).OrderByDescending(k => k.CreatedAt).ToListAsync();

    public Task<ApiKey?> GetByIdForCurrentOrgAsync(string id) =>
        _db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id);

    // A handful of rows per org, so the names come back and the caller matches
    // them in code. The tenant filter keeps it to the current org.
    public Task<List<string>> GetActiveNamesForCurrentOrgAsync() =>
        _db.ApiKeys.Where(k => k.Active && k.ActingUserId == null).Select(k => k.Name).ToListAsync();

    public async Task AddAsync(ApiKey key)
    {
        key.CreatedAt = key.CreatedAt == default ? DateTime.UtcNow : key.CreatedAt;
        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync();
    }

    public async Task<ActingMember?> GetActingMemberAsync(string organizationId, string userId)
    {
        var role = await _db.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && m.UserId == userId)
            .Select(m => m.Role)
            .FirstOrDefaultAsync();
        if (role is null) return null;

        var email = await _db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync();
        return email is null ? null : new ActingMember(role, email);
    }

    public async Task AddActingKeyAsync(ApiKey key)
    {
        var now = DateTime.UtcNow;
        // Kept a while after expiry so the API-key request log can still name
        // the key; nothing refers to it after that.
        var cutoff = now.AddDays(-30);
        _db.ApiKeys.RemoveRange(await _db.ApiKeys
            .Where(k => k.ActingUserId != null && k.ExpiresAt < cutoff)
            .ToListAsync());

        key.CreatedAt = key.CreatedAt == default ? now : key.CreatedAt;
        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync();
    }

    public async Task RevokeActingKeyAsync(string id)
    {
        // By id, whatever org the request is in: the turn that made it ends it.
        var key = await _db.ApiKeys.IgnoreQueryFilters()
            .FirstOrDefaultAsync(k => k.Id == id && k.ActingUserId != null);
        if (key is null || !key.Active) return;
        key.Active = false;
        await _db.SaveChangesAsync();
    }

    public async Task RevokeActingKeysAsync(string userId)
    {
        foreach (var live in await _db.ApiKeys
                     .Where(k => k.ActingUserId == userId && k.Active)
                     .ToListAsync())
            live.Active = false;
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
