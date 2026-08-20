using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiKeys.Dtos;
using AltomateHR.Api.Modules.ApiKeys.Entities;

namespace AltomateHR.Api.Tests.ApiKeys;

public class ApiKeyServiceTests
{
    [Fact]
    public async Task CreateAsync_ReturnsRawTokenOnce_ButStoresOnlyTheHash()
    {
        var repo = new FakeApiKeyRepository();
        var service = new ApiKeyService(repo);

        var created = await service.CreateAsync(new CreateApiKeyDto
        {
            Name = "ABPay importer",
            Scopes = ["employees:read", "claims:read"],
        });

        Assert.StartsWith("wp_live_", created.Token);              // raw token returned once
        var stored = Assert.Single(repo.Keys);
        Assert.NotEqual(created.Token, stored.TokenHash);          // only the hash persisted
        Assert.Equal(ApiTokenGenerator.HashToken(created.Token), stored.TokenHash);
        Assert.Equal("employees:read,claims:read", stored.Scopes);
    }

    [Fact]
    public async Task CreateAsync_RejectsUnknownScopes()
    {
        var service = new ApiKeyService(new FakeApiKeyRepository());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateAsync(new CreateApiKeyDto { Name = "x", Scopes = ["employees:read", "not:a:scope"] }));
    }

    [Fact]
    public async Task GetAllAsync_NeverExposesTheToken()
    {
        var repo = new FakeApiKeyRepository();
        var service = new ApiKeyService(repo);
        await service.CreateAsync(new CreateApiKeyDto { Name = "k1", Scopes = ["leave:read"] });

        var listed = Assert.Single(await service.GetAllAsync());

        Assert.Equal("k1", listed.Name);
        Assert.Contains("leave:read", listed.Scopes);
        // ApiKeyDto has no Token property at all — nothing to leak.
        Assert.IsType<ApiKeyDto>(listed, exactMatch: true);
    }

    [Fact]
    public async Task RevokeAsync_SoftDeletesTheKey()
    {
        var repo = new FakeApiKeyRepository();
        var service = new ApiKeyService(repo);
        var created = await service.CreateAsync(new CreateApiKeyDto { Name = "k", Scopes = [] });

        var ok = await service.RevokeAsync(created.Id);

        Assert.True(ok);
        Assert.False(repo.Keys.Single().Active);
    }

    [Fact]
    public async Task RevokeAsync_ReturnsFalseForUnknownKey()
    {
        var service = new ApiKeyService(new FakeApiKeyRepository());

        Assert.False(await service.RevokeAsync("does-not-exist"));
    }
}

// In-memory repo standing in for AppDbContext. The service never touches EF directly,
// so this fully exercises its logic.
internal sealed class FakeApiKeyRepository : IApiKeyRepository
{
    public List<ApiKey> Keys { get; } = new();

    public Task<ApiKey?> GetByHashAsync(string tokenHash) =>
        Task.FromResult(Keys.FirstOrDefault(k => k.TokenHash == tokenHash));

    public Task<List<ApiKey>> GetForCurrentOrgAsync() =>
        Task.FromResult(Keys.OrderByDescending(k => k.CreatedAt).ToList());

    public Task<ApiKey?> GetByIdForCurrentOrgAsync(string id) =>
        Task.FromResult(Keys.FirstOrDefault(k => k.Id == id));

    public Task AddAsync(ApiKey key)
    {
        Keys.Add(key);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ApiKey key) => Task.CompletedTask;   // mutates in place (same ref)

    public Task RecordUsageAsync(ApiKeyAuditLog log) => Task.CompletedTask;
}
