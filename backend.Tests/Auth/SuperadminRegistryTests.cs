using AltomateHR.Api.Modules.Auth;
using Microsoft.Extensions.Configuration;

namespace AltomateHR.Api.Tests.Auth;

public class SuperadminRegistryTests
{
    private static SuperadminRegistry Build(string? emails)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SUPERADMIN_EMAILS"] = emails })
            .Build();
        return new SuperadminRegistry(config);
    }

    [Fact]
    public void WhitelistedEmail_IsSuperadmin_CaseInsensitiveAndTrimmed()
    {
        var reg = Build(" Ops@fusioneta.com , admin@altomate.com ");
        Assert.True(reg.IsSuperadmin("ops@fusioneta.com"));
        Assert.True(reg.IsSuperadmin("ADMIN@altomate.com"));
    }

    [Fact]
    public void NonWhitelisted_OrNull_IsNotSuperadmin()
    {
        var reg = Build("ops@fusioneta.com");
        Assert.False(reg.IsSuperadmin("owner@customer.com"));   // a customer Owner is NOT superadmin
        Assert.False(reg.IsSuperadmin(null));
        Assert.False(reg.IsSuperadmin(""));
    }

    [Fact]
    public void EmptyWhitelist_NobodyIsSuperadmin()
    {
        var reg = Build(null);
        Assert.False(reg.IsSuperadmin("anyone@anywhere.com"));
    }
}
