using AltomateHR.Api.Modules.ApiKeys.Entities;

namespace AltomateHR.Api.Modules.ApiKeys;

public interface IApiKeyRepository
{
    // Auth lookup — runs before the org is known, so it crosses orgs (see impl).
    Task<ApiKey?> GetByHashAsync(string tokenHash);

    // Management (current org only, via the global tenant filter).
    Task<List<ApiKey>> GetForCurrentOrgAsync();
    Task<ApiKey?> GetByIdForCurrentOrgAsync(string id);

    // Names of the current org's ACTIVE keys (tenant filter scopes it).
    Task<List<string>> GetActiveNamesForCurrentOrgAsync();

    Task AddAsync(ApiKey key);

    // ---- acting (FAH) keys ----

    // The person a FAH key acts for, while they're still a member of the key's
    // company: their role there and email. Null once they've been removed.
    // Crosses orgs — it runs during authentication, before the org is known.
    Task<ActingMember?> GetActingMemberAsync(string organizationId, string userId);

    // Adds a FAH key, clearing out long-expired ones while it's there.
    Task AddActingKeyAsync(ApiKey key);

    // Retires one FAH key — FAH ends each key with the chat turn it was made for.
    Task RevokeActingKeyAsync(string id);

    // Retires every live FAH key the person holds in this company.
    Task RevokeActingKeysAsync(string userId);
    Task UpdateAsync(ApiKey key);

    // Best-effort: write one audit row + bump LastUsedAt in a single save.
    Task RecordUsageAsync(ApiKeyAuditLog log);
}

public sealed record ActingMember(string Role, string Email);
