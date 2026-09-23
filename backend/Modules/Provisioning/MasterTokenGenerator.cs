using System.Security.Cryptography;
using System.Text;

namespace AltomateHR.Api.Modules.Provisioning;

// Same construction as ApiTokenGenerator, different prefix — so a master token
// is recognisable on sight and can never be mistaken for an org-scoped one in a
// log, a config file or a support ticket.
public static class MasterTokenGenerator
{
    public const string Prefix = "wp_master_";

    public static (string Raw, string Hash, string DisplayPrefix) Generate()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var raw = Prefix + secret;
        return (raw, HashToken(raw), raw[..18]);
    }

    // SHA-256 for the same reason as ApiTokenGenerator: the token is already
    // high-entropy random, so a slow KDF would burn CPU on every request without
    // adding protection.
    public static string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
}
