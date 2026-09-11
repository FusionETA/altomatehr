using System.Security.Cryptography;
using System.Text;

namespace AltomateHR.Api.Common;

// Symmetric encryption at rest, for secrets the app must be able to READ BACK
// — currently the statutory-portal logins an admin saves so they do not have
// to fish them out of a password manager every filing deadline.
//
// ⚠️ This is NOT for passwords the app authenticates against. Those are
// hashed and never recovered; see PasswordHasher. Reversible encryption is
// correct here only because the whole point is showing the admin the value
// again.
//
// AES-256-GCM, so tampering is detected rather than silently decrypting to
// garbage. Layout of the stored base64 blob:
//
//     iv (12 bytes) || authTag (16 bytes) || ciphertext (variable)
//
// The IV is fresh-random per call, so encrypting the same value twice
// produces different blobs — a reader cannot tell two orgs share a password.
public class SecretBox : ISecretBox
{
    private const int IvBytes = 12;
    private const int AuthTagBytes = 16;

    private readonly byte[] _key;

    public SecretBox(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Secrets:PortalCredentialsKey"];

        if (string.IsNullOrWhiteSpace(configured))
        {
            // Refusing to start is the right failure: silently falling back
            // to a derived key in production would encrypt real credentials
            // under a value anyone with the source can reproduce.
            if (environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Secrets:PortalCredentialsKey must be set in production — saved portal "
                    + "credentials are encrypted with it. Add it to user-secrets or the "
                    + "environment.");
            }

            // Development only, so a contributor can use the feature without
            // being forced to set an env var first.
            configured = "altomatehr-development-portal-credentials-key";
        }

        // Any length of configured string becomes a 32-byte key.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
    }

    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return null;

        var iv = RandomNumberGenerator.GetBytes(IvBytes);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plainBytes.Length];
        var tag = new byte[AuthTagBytes];

        using var aes = new AesGcm(_key, AuthTagBytes);
        aes.Encrypt(iv, plainBytes, ciphertext, tag);

        return Convert.ToBase64String([.. iv, .. tag, .. ciphertext]);
    }

    // Null for anything that will not decrypt — a blob written under a
    // previous key, or a corrupted one. The caller shows the field as unset
    // rather than the page failing: an unreadable stored password is a
    // nuisance, and taking the credentials tab down over it is worse.
    public string? Decrypt(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return null;

        try
        {
            var blob = Convert.FromBase64String(stored);
            if (blob.Length <= IvBytes + AuthTagBytes) return null;

            var iv = blob.AsSpan(0, IvBytes);
            var tag = blob.AsSpan(IvBytes, AuthTagBytes);
            var ciphertext = blob.AsSpan(IvBytes + AuthTagBytes);
            var plainBytes = new byte[ciphertext.Length];

            using var aes = new AesGcm(_key, AuthTagBytes);
            aes.Decrypt(iv, ciphertext, tag, plainBytes);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}
