using AltomateHR.Api.Modules.ApiKeys.Dtos;
using AltomateHR.Api.Modules.ApiKeys.Entities;

namespace AltomateHR.Api.Modules.ApiKeys;

public class ApiKeyService : IApiKeyService
{
    private readonly IApiKeyRepository _repo;

    public ApiKeyService(IApiKeyRepository repo) => _repo = repo;

    public async Task<CreatedApiKeyDto> CreateAsync(CreateApiKeyDto dto)
    {
        // Validate scopes against the catalog — reject anything unknown up front.
        var scopes = dto.Scopes.Distinct().ToList();
        var unknown = scopes.Where(s => !ApiScopes.IsKnown(s)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown scope(s): {string.Join(", ", unknown)}.");

        var (raw, hash, prefix) = ApiTokenGenerator.Generate();

        var key = new ApiKey
        {
            // OrganizationId left blank → StampTenant sets it to the Owner's active org.
            Name = dto.Name.Trim(),
            TokenHash = hash,
            TokenPrefix = prefix,
            Scopes = ApiScopes.Join(scopes),
            Active = true,
            CreatedAt = DateTime.UtcNow,
        };
        await _repo.AddAsync(key);

        // The ONLY time the raw token leaves the server.
        return new CreatedApiKeyDto
        {
            Id = key.Id,
            Name = key.Name,
            TokenPrefix = key.TokenPrefix,
            Scopes = scopes,
            Active = key.Active,
            CreatedAt = key.CreatedAt,
            LastUsedAt = null,
            Token = raw,
        };
    }

    public async Task<IReadOnlyList<ApiKeyDto>> GetAllAsync()
    {
        var keys = await _repo.GetForCurrentOrgAsync();
        return keys.Select(ToDto).ToList();
    }

    public async Task<bool> RevokeAsync(string id)
    {
        var key = await _repo.GetByIdForCurrentOrgAsync(id);
        if (key is null) return false;

        if (key.Active)
        {
            key.Active = false;
            await _repo.UpdateAsync(key);
        }
        return true;
    }

    private static ApiKeyDto ToDto(ApiKey k) => new()
    {
        Id = k.Id,
        Name = k.Name,
        TokenPrefix = k.TokenPrefix,
        Scopes = ApiScopes.Split(k.Scopes),
        Active = k.Active,
        CreatedAt = k.CreatedAt,
        LastUsedAt = k.LastUsedAt,
    };
}
