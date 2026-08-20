using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Tests.ApiKeys;

public class ApiTokenGeneratorTests
{
    [Fact]
    public void Generate_ProducesWpLivePrefixedToken()
    {
        var (raw, _, displayPrefix) = ApiTokenGenerator.Generate();

        Assert.StartsWith("wp_live_", raw);
        Assert.Equal(16, displayPrefix.Length);          // "wp_live_" (8) + 8 hex
        Assert.StartsWith(displayPrefix, raw);
    }

    [Fact]
    public void Generate_ProducesUniqueTokens()
    {
        var (a, _, _) = ApiTokenGenerator.Generate();
        var (b, _, _) = ApiTokenGenerator.Generate();

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void HashToken_IsDeterministicAndNotTheRawToken()
    {
        var (raw, hash, _) = ApiTokenGenerator.Generate();

        Assert.Equal(hash, ApiTokenGenerator.HashToken(raw));  // stable
        Assert.NotEqual(raw, hash);                            // never store the raw token
        Assert.Equal(64, hash.Length);                         // SHA-256 hex
    }
}
