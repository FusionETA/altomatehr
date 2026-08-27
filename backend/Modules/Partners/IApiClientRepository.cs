using AltomateHR.Api.Modules.Partners.Entities;

namespace AltomateHR.Api.Modules.Partners;

// Data access for the partner-app registry. ApiClient is global config (not
// tenant-scoped), so these lookups run regardless of the current org.
public interface IApiClientRepository
{
    Task<ApiClient?> GetByIdAsync(string id);

    // By registry name / launch slug (case-insensitive). Caller checks Active.
    Task<ApiClient?> GetByNameAsync(string name);

    // The per-request client identification: hash the presented secret, look it up.
    Task<ApiClient?> GetBySecretHashAsync(string secretHash);

    Task<ApiClient> AddAsync(ApiClient client);
}
