using Microsoft.Extensions.Caching.Memory;

namespace AltomateHR.Api.Modules.Organizations;

public class OrganizationStatus : IOrganizationStatus
{
    private const string CacheKey = "organizations:deactivated";

    // Long enough that a busy minute reads the database once, short enough
    // that a second API instance (which Changed() can't reach) catches up fast.
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly IOrganizationRepository _organizations;
    private readonly IMemoryCache _cache;

    public OrganizationStatus(IOrganizationRepository organizations, IMemoryCache cache)
    {
        _organizations = organizations;
        _cache = cache;
    }

    public async Task<bool> IsDeactivatedAsync(string organizationId) =>
        (await DeactivatedIdsAsync()).Contains(organizationId);

    public async Task<IReadOnlySet<string>> DeactivatedIdsAsync()
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlySet<string>? ids) && ids is not null) return ids;

        ids = (await _organizations.GetDeactivatedIdsAsync()).ToHashSet(StringComparer.Ordinal);
        _cache.Set(CacheKey, ids, Ttl);
        return ids;
    }

    public void Changed() => _cache.Remove(CacheKey);
}
