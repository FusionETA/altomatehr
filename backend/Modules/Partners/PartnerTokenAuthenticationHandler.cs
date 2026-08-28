using System.Security.Claims;
using System.Text.Encodings.Web;
using AltomateHR.Api.Modules.ApiKeys;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Partners;

// Validates an apx_live_ partner access token (looked up in Redis) and turns it
// into a per-request identity — the .NET twin of ApiKeyAuthenticationHandler, but
// for the partner-integration flow.
//
// The principal it builds mirrors a JWT login (sub / org / role) so the EXISTING
// org-scoped, role-gated GET /employees works unchanged: org drives the tenant
// filter; Role=Admin clears the [Authorize(Roles=...)] gate. Role=Admin alone
// would be TOO broad, so PartnerAccessFilter re-narrows partner callers to only
// endpoints whose declared scope the token actually holds (deny-by-default).
public class PartnerTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IPartnerAuthStore _store;

    public PartnerTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IPartnerAuthStore store)
        : base(options, logger, encoder)
    {
        _store = store;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var raw = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : header.Trim();

        // Not an apx_live_ token → fall through cleanly.
        if (string.IsNullOrEmpty(raw) || !raw.StartsWith(PartnerTokenGenerator.AccessTokenPrefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var data = await _store.GetAccessTokenAsync(raw);
        if (data is null)
            return AuthenticateResult.Fail("Invalid or expired access token.");   // includes revoked (key deleted)

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, data.UserId),
            new("sub", data.UserId),
            new("org", data.OrganizationId),                     // → tenant filter scopes to this org
            new(ClaimTypes.Role, "Admin"),                       // clears role gates; re-narrowed by PartnerAccessFilter
            new(PartnerAuthenticationDefaults.ClientIdClaim, data.ClientId),
            new("aud", data.Audience),
        };
        foreach (var scope in ApiScopes.Split(data.Scopes))
            claims.Add(new Claim(ApiKeyAuthenticationDefaults.ScopeClaim, scope));   // reuse the shared scope claim

        var identity = new ClaimsIdentity(claims, PartnerAuthenticationDefaults.Scheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), PartnerAuthenticationDefaults.Scheme);
        return AuthenticateResult.Success(ticket);
    }
}
