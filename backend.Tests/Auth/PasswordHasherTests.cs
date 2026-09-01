using AltomateHR.Api.Modules.Auth;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Tests.Auth;

public class PasswordHasherTests
{
    // A REAL scrypt hash produced by the MONOLITH's own code — generated with
    // node:crypto  scryptSync("Correct-Horse-42", saltHex, 64)  (default N/r/p).
    // If the .NET scrypt verifier isn't byte-for-byte compatible with the old
    // system, this test fails — which is the whole point of hard-coding it.
    internal const string LegacyPassword = "Correct-Horse-42";
    internal const string LegacyScryptHash =
        "4356dcccf04d80e541c99c6de164750e:d07d4f2426c42cb8634797b1d869c151b7c0d82ae487b43ac018c04f69bebab80344834c82d4b0d04d067b7beb72acbabed7287a88db9a4b833ebb6dd342395e";

    [Fact]
    public void Verify_LegacyScryptHash_FromMonolith_Succeeds() =>
        Assert.True(PasswordHasher.Verify(LegacyPassword, LegacyScryptHash));

    [Fact]
    public void Verify_LegacyScryptHash_WrongPassword_Fails() =>
        Assert.False(PasswordHasher.Verify("wrong-password", LegacyScryptHash));

    [Fact]
    public void Verify_BcryptHash_Succeeds()
    {
        var hash = BC.HashPassword("hunter2");
        Assert.True(PasswordHasher.Verify("hunter2", hash));
        Assert.False(PasswordHasher.Verify("nope", hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-hash")]        // no separator, not bcrypt
    [InlineData("garbage:nothex")]         // scrypt-shaped, but the key isn't valid hex
    [InlineData("$2a$totally-malformed")]  // bcrypt-shaped, but broken
    public void Verify_MalformedHash_ReturnsFalse_NeverThrows(string badHash) =>
        Assert.False(PasswordHasher.Verify("anything", badHash));   // must not throw (previously → 500)

    [Fact]
    public void IsLegacyScrypt_DetectsFormat()
    {
        Assert.True(PasswordHasher.IsLegacyScrypt(LegacyScryptHash));
        Assert.False(PasswordHasher.IsLegacyScrypt(BC.HashPassword("x")));   // $2… bcrypt
        Assert.False(PasswordHasher.IsLegacyScrypt(""));
        Assert.False(PasswordHasher.IsLegacyScrypt(null));
    }
}
