using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.ApiKeys;

// The per-request key lookup. An integration (ABPay) decides a company has
// been deleted by its key being rejected, so a key whose company no longer
// exists must not authenticate — even if the key row was left behind.
public class ApiKeyLookupTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ApiKeyRepository _keys;

    public ApiKeyLookupTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"api-key-lookup-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, new StubCurrentUser());
        _keys = new ApiKeyRepository(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync(bool withOrganization)
    {
        if (withOrganization) _db.Organizations.Add(new Organization { Id = "org-1", Name = "Acme Engineering" });
        _db.ApiKeys.Add(new ApiKey { OrganizationId = "org-1", Name = "ABPay", TokenHash = "hash-1", TokenPrefix = "wp_live_x" });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task AKeyForAnExistingCompany_IsFound()
    {
        await SeedAsync(withOrganization: true);

        Assert.NotNull(await _keys.GetByHashAsync("hash-1"));
    }

    [Fact]
    public async Task AKeyWhoseCompanyIsGone_IsTreatedAsUnknown()
    {
        await SeedAsync(withOrganization: false);

        Assert.Null(await _keys.GetByHashAsync("hash-1"));
    }
}
