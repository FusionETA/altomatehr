using AltomateHR.Api.Modules.Partners.Dtos;

namespace AltomateHR.Api.Modules.Partners;

public interface IPartnerAuthService
{
    // Phase A step 1: a signed-in user launches into {app}. Mints a single-use
    // ticket for the caller's org and returns the full redirect URL (the app's
    // registered redirectUrl + ?t=<ticket>). Null if the app is unknown/inactive.
    Task<string?> MintLaunchTicketAsync(string appName, string userId, string organizationId);

    // Phase A step 3: client secret + ticket → scoped access (+ refresh) token.
    // Null on any failure (bad secret, unknown/expired/foreign ticket) — the caller
    // maps that to 401 without leaking which check failed.
    Task<PartnerTokenResponseDto?> RedeemTicketAsync(string clientSecret, string ticket);

    // Phase B step 6: client secret + refresh token → a fresh access token. The
    // refresh token rotates (single-use). Null on failure.
    Task<PartnerTokenResponseDto?> RefreshAsync(string clientSecret, string refreshToken);
}
