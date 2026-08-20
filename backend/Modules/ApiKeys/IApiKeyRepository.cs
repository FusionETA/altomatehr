using AltomateHR.Api.Modules.ApiKeys.Entities;

namespace AltomateHR.Api.Modules.ApiKeys;

public interface IApiKeyRepository
{
    // Auth lookup — runs before the org is known, so it crosses orgs (see impl).
    Task<ApiKey?> GetByHashAsync(string tokenHash);

    // Management (current org only, via the global tenant filter).
    Task<List<ApiKey>> GetForCurrentOrgAsync();
    Task<ApiKey?> GetByIdForCurrentOrgAsync(string id);

    Task AddAsync(ApiKey key);
    Task UpdateAsync(ApiKey key);

    // Best-effort: write one audit row + bump LastUsedAt in a single save.
    Task RecordUsageAsync(ApiKeyAuditLog log);
}
