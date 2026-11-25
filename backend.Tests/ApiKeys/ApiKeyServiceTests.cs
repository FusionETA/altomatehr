using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiKeys.Dtos;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Tests.Audit;

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
    public async Task UpdateAsync_ChangesScopesAndName_KeepsTheToken_AndAudits()
    {
        var repo = new FakeApiKeyRepository();
        var audit = new FakeAuditService();
        var service = new ApiKeyService(repo, audit);
        var created = await service.CreateAsync(new CreateApiKeyDto
        {
            Name = "3D Floor",
            Scopes = ["employees:read", "organizations:write"],
        });
        var hashBefore = repo.Keys.Single().TokenHash;

        var updated = await service.UpdateAsync(created.Id, new UpdateApiKeyDto
        {
            Name = " 3D Floor app ",
            Scopes = ["employees:read", "attendance:read"],
        });

        Assert.NotNull(updated);
        Assert.Equal("3D Floor app", updated!.Name);
        Assert.Equal(["employees:read", "attendance:read"], updated.Scopes);
        var stored = repo.Keys.Single();
        Assert.Equal("employees:read,attendance:read", stored.Scopes);
        Assert.Equal(hashBefore, stored.TokenHash);                  // same token keeps working
        Assert.IsType<ApiKeyDto>(updated, exactMatch: true);         // no token in the reply

        var entry = Assert.Single(audit.Written);
        Assert.Equal(AuditActions.ApiKeyUpdate, entry.Action);
        Assert.Contains("added attendance:read", entry.Summary);
        Assert.Contains("removed organizations:write", entry.Summary);
        Assert.Contains("renamed from \"3D Floor\"", entry.Summary);
    }

    [Fact]
    public async Task UpdateAsync_NoChange_WritesNoAuditEntry()
    {
        var repo = new FakeApiKeyRepository();
        var audit = new FakeAuditService();
        var service = new ApiKeyService(repo, audit);
        var created = await service.CreateAsync(new CreateApiKeyDto { Name = "k", Scopes = ["leave:read"] });

        await service.UpdateAsync(created.Id, new UpdateApiKeyDto { Name = "k", Scopes = ["leave:read"] });

        Assert.Empty(audit.Written);
    }

    [Fact]
    public async Task UpdateAsync_RejectsUnknownScopes_AndLeavesTheKeyAlone()
    {
        var repo = new FakeApiKeyRepository();
        var service = new ApiKeyService(repo);
        var created = await service.CreateAsync(new CreateApiKeyDto { Name = "k", Scopes = ["leave:read"] });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateAsync(created.Id, new UpdateApiKeyDto { Name = "k", Scopes = ["leave:read", "root:all"] }));
        Assert.Equal("leave:read", repo.Keys.Single().Scopes);
    }

    [Fact]
    public async Task UpdateAsync_RefusesARevokedKey()
    {
        var repo = new FakeApiKeyRepository();
        var service = new ApiKeyService(repo);
        var created = await service.CreateAsync(new CreateApiKeyDto { Name = "k", Scopes = ["leave:read"] });
        await service.RevokeAsync(created.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(created.Id, new UpdateApiKeyDto { Name = "k", Scopes = ["payroll:write"] }));
        Assert.Equal("leave:read", repo.Keys.Single().Scopes);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNullForUnknownOrAssistantKey()
    {
        var repo = new FakeApiKeyRepository();
        repo.Keys.Add(new ApiKey { Id = "fah", Name = "assistant", Scopes = "leave:read", Active = true, ActingUserId = "u1" });
        var service = new ApiKeyService(repo);

        Assert.Null(await service.UpdateAsync("does-not-exist", new UpdateApiKeyDto { Name = "x", Scopes = [] }));
        Assert.Null(await service.UpdateAsync("fah", new UpdateApiKeyDto { Name = "x", Scopes = ["payroll:write"] }));
        Assert.Equal("leave:read", repo.Keys.Single().Scopes);
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

    public Task<List<string>> GetActiveNamesForCurrentOrgAsync() =>
        Task.FromResult(Keys.Where(k => k.Active).Select(k => k.Name).ToList());

    public Task AddAsync(ApiKey key)
    {
        Keys.Add(key);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ApiKey key) => Task.CompletedTask;   // mutates in place (same ref)

    public Task RecordUsageAsync(ApiKeyAuditLog log) => Task.CompletedTask;

    // (organizationId, userId) → member, for FAH keys.
    public Dictionary<(string Org, string User), ActingMember> Members { get; } = new();

    public Task<ActingMember?> GetActingMemberAsync(string organizationId, string userId) =>
        Task.FromResult(Members.TryGetValue((organizationId, userId), out var m) ? m : null);

    public Task AddActingKeyAsync(ApiKey key)
    {
        Keys.Add(key);
        return Task.CompletedTask;
    }

    public Task RevokeActingKeyAsync(string id)
    {
        foreach (var live in Keys.Where(k => k.Id == id && k.ActingUserId != null)) live.Active = false;
        return Task.CompletedTask;
    }

}
