using AltomateHR.Api.Modules.ApiKeys.Dtos;

namespace AltomateHR.Api.Modules.ApiKeys;

public interface IApiKeyService
{
    // Create a key for the caller's active org. Returns the raw token ONCE.
    Task<CreatedApiKeyDto> CreateAsync(CreateApiKeyDto dto);

    Task<IReadOnlyList<ApiKeyDto>> GetAllAsync();

    // Soft-revoke (Active=false). True if the key exists in this org, false if not.
    Task<bool> RevokeAsync(string id);
}
