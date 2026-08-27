using System.Security.Cryptography;
using System.Text;

namespace AltomateHR.Api.Modules.Partners;

// Opaque credential generation for the partner-integration flow. Every value is a
// high-entropy random string, so we compare/store only its SHA-256 hash — a slow
// KDF (bcrypt) would burn CPU without adding protection (same reasoning as
// ApiTokenGenerator for wp_live_ keys).
//
// Prefixes are the visible tag that also drives auth-scheme routing (apx_live_ →
// PartnerToken handler) and single-glance identification in logs.
public static class PartnerTokenGenerator
{
    public const string ClientSecretPrefix = "altomate_sk_";   // we issue this to the app
    public const string AccessTokenPrefix  = "apx_live_";       // scoped, ~15 min
    public const string RefreshTokenPrefix = "apx_ref_";        // mints new access tokens
    public const string TicketPrefix       = "tkt_";            // single-use launch ticket, 120 s

    public static string NewClientSecret() => ClientSecretPrefix + RandomHex(24);
    public static string NewAccessToken()  => AccessTokenPrefix  + RandomHex(24);
    public static string NewRefreshToken() => RefreshTokenPrefix + RandomHex(24);
    public static string NewTicket()       => TicketPrefix       + RandomHex(16);

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();

    private static string RandomHex(int bytes) =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
