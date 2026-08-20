using System.Security.Cryptography;
using System.Text;

namespace AltomateHR.Api.Modules.ApiKeys;

// Generates + hashes wp_live_ machine tokens. Format: wp_live_<48 hex chars> (24
// random bytes). The prefix is the visible tag; the rest is the secret.
public static class ApiTokenGenerator
{
    public const string Prefix = "wp_live_";

    // Returns the RAW token (shown once), its stored HASH, and a short display prefix
    // ("wp_live_" + 8 hex) kept in plaintext so the Owner can recognise the key later.
    public static (string Raw, string Hash, string DisplayPrefix) Generate()
    {
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var raw = Prefix + secret;
        return (raw, HashToken(raw), raw[..16]);
    }

    // SHA-256 is the right choice here: the token is already high-entropy random, so a
    // slow KDF (bcrypt) would only burn CPU on every request without adding protection.
    public static string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
}
