using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.ApiKeys;

// "Is this company connected to ABPay?" — an ACTIVE API key in the current org
// whose name contains "ABPay". Gates the AB Pay timesheet export. Run against a
// real EF context so the tenant filter does the org scoping for real.
public class AbPayIntegrationTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new() { OrganizationId = "org-1" };
    private readonly ApiKeyService _service;

    public AbPayIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"abpay-integration-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _service = new ApiKeyService(new ApiKeyRepository(_db));
    }

    public void Dispose() => _db.Dispose();

    private async Task AddKeyAsync(string name, bool active = true, string organizationId = "org-1")
    {
        _db.ApiKeys.Add(new ApiKey
        {
            OrganizationId = organizationId,
            Name = name,
            TokenHash = Guid.NewGuid().ToString("N"),
            TokenPrefix = "wp_live_x",
            Active = active,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task AnActiveAbPayKey_Enables()
    {
        await AddKeyAsync("ABPay importer");

        Assert.True(await _service.HasAbPayIntegrationAsync());
    }

    [Fact]
    public async Task NoKeys_DoesNotEnable() =>
        Assert.False(await _service.HasAbPayIntegrationAsync());

    // Revoking the key disconnects ABPay, and the export goes with it.
    [Fact]
    public async Task ARevokedAbPayKey_DoesNotEnable()
    {
        await AddKeyAsync("ABPay importer", active: false);

        Assert.False(await _service.HasAbPayIntegrationAsync());
    }

    [Fact]
    public async Task AKeyForAnotherIntegration_DoesNotEnable()
    {
        await AddKeyAsync("Xero sync");

        Assert.False(await _service.HasAbPayIntegrationAsync());
    }

    // Another company's ABPay key says nothing about this one.
    [Fact]
    public async Task AnotherOrgsAbPayKey_DoesNotEnable()
    {
        await AddKeyAsync("ABPay importer", organizationId: "org-2");

        Assert.False(await _service.HasAbPayIntegrationAsync());
    }

    [Fact]
    public async Task OneMatchingKeyAmongOthers_Enables()
    {
        await AddKeyAsync("Xero sync");
        await AddKeyAsync("AB Pay", active: false);
        await AddKeyAsync("ab-pay importer");

        Assert.True(await _service.HasAbPayIntegrationAsync());
    }

    [Theory]
    [InlineData("ABPay importer", true)]
    [InlineData("abpay", true)]
    [InlineData("Importer for ABPAY", true)]
    [InlineData("AB Pay importer", true)]
    [InlineData("ab-pay", true)]
    [InlineData("AB_Pay", true)]
    [InlineData("Xero sync", false)]
    [InlineData("AB importer", false)]
    [InlineData("Pay AB", false)]
    [InlineData("ABPayRun", true)]
    [InlineData("GrabPay payouts", false)]
    [InlineData("Grab Pay", false)]
    [InlineData("Fab Payments", false)]
    [InlineData("Kab-Payroll", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void KeyName_Matching(string? name, bool expected) =>
        Assert.Equal(expected, ApiKeyService.IsAbPayKeyName(name));
}
