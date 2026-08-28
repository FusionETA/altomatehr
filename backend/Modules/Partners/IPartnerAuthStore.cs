namespace AltomateHR.Api.Modules.Partners;

// What a launch ticket carries while it waits (120 s) to be redeemed.
public record PartnerTicketData(string ClientId, string UserId, string OrganizationId);

// What an access/refresh token resolves to. Scopes + Audience come from the
// ApiClient row at issue time and are frozen into the token's lifetime.
public record PartnerTokenData(
    string ClientId, string UserId, string OrganizationId, string Scopes, string Audience);

// The ephemeral half of the partner flow, backed by Redis (IDistributedCache) with
// TTLs. Tickets and tokens auto-expire; nothing here is durable. The ApiClient
// registry (durable config) lives in MySQL instead.
public interface IPartnerAuthStore
{
    // Mint + store a single-use ticket; returns the raw ticket id.
    Task<string> MintTicketAsync(PartnerTicketData data, TimeSpan ttl);

    // Redeem = read AND delete (single-use). Null if unknown/expired/already spent.
    Task<PartnerTicketData?> RedeemTicketAsync(string ticket);

    Task StoreAccessTokenAsync(string token, PartnerTokenData data, TimeSpan ttl);
    Task<PartnerTokenData?> GetAccessTokenAsync(string token);

    Task StoreRefreshTokenAsync(string token, PartnerTokenData data, TimeSpan ttl);

    // Redeem = read AND delete, so refresh tokens rotate: the presented one dies as
    // a fresh one is issued, and a stolen-then-reused token surfaces as an invalid.
    Task<PartnerTokenData?> RedeemRefreshTokenAsync(string token);
}
