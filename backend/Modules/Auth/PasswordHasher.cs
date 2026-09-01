using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Modules.Auth;

// Verifies a password against a stored hash in EITHER format the system holds:
//   • BCrypt  ("$2…")                  — the .NET app's native format
//   • scrypt  "<saltHex>:<keyHex>"     — legacy hashes migrated from the Next.js
//                                        monolith (lib/auth/password.ts)
//
// The scrypt path reproduces the monolith EXACTLY: node:crypto scryptSync with the
// default cost params (N=16384, r=8, p=1), a 64-byte key, and — the subtle part —
// the salt is the hex STRING passed straight to scryptSync, so its UTF-8 bytes are
// the salt (NOT the decoded 16 bytes). Verified byte-for-byte against a Node-produced
// reference hash in the tests.
//
// Every path is exception-safe: a malformed/unknown hash returns false (a failed
// login), never a thrown exception (which previously surfaced as a 500).
public static class PasswordHasher
{
    // Node's scryptSync defaults + KEY_LENGTH from the monolith.
    private const int ScryptCost = 16384;   // N
    private const int ScryptBlockSize = 8;  // r
    private const int ScryptParallel = 1;   // p
    private const int ScryptKeyLength = 64;

    public static string HashBcrypt(string password) => BC.HashPassword(password);

    // A legacy scrypt hash — not BCrypt, and shaped "<hex>:<hex>". The caller uses
    // this to decide whether to transparently re-hash to BCrypt after a match.
    public static bool IsLegacyScrypt(string? hash) =>
        !string.IsNullOrEmpty(hash) && !hash.StartsWith("$2", StringComparison.Ordinal) && hash.Contains(':');

    public static bool Verify(string password, string? storedHash)
    {
        if (string.IsNullOrEmpty(storedHash)) return false;
        try
        {
            if (storedHash.StartsWith("$2", StringComparison.Ordinal))
                return BC.Verify(password, storedHash);      // BCrypt
            if (IsLegacyScrypt(storedHash))
                return VerifyScrypt(password, storedHash);   // legacy scrypt
            return false;                                     // unknown format
        }
        catch
        {
            return false;   // any malformed hash → failed login, never a 500
        }
    }

    private static bool VerifyScrypt(string password, string storedHash)
    {
        var sep = storedHash.IndexOf(':');
        var saltHex = storedHash[..sep];
        var keyHex = storedHash[(sep + 1)..];
        if (saltHex.Length == 0 || keyHex.Length == 0) return false;

        // The monolith passes the salt to scryptSync as the hex STRING, so scrypt
        // sees its UTF-8 bytes as the salt. Password is likewise its UTF-8 bytes.
        var saltBytes = Encoding.UTF8.GetBytes(saltHex);
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        var derived = SCrypt.Generate(
            passwordBytes, saltBytes, ScryptCost, ScryptBlockSize, ScryptParallel, ScryptKeyLength);
        var expected = Convert.FromHexString(keyHex);

        return CryptographicOperations.FixedTimeEquals(derived, expected);
    }
}
