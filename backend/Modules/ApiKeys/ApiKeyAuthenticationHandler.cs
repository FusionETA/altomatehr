using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.ApiKeys.Entities;

namespace AltomateHR.Api.Modules.ApiKeys;

// Verifies a wp_live_ token and turns it into a per-request identity. This is the
// .NET twin of the monolith's authenticateApiRequest(): it runs INSIDE
// app.UseAuthentication(), before authorization and before the controller.
//
// It builds a ClaimsPrincipal (server-side, this-request-only — never sent back) with
// the SAME claim shape a JWT login produces, so every existing controller/service works
// unchanged: the key's org becomes the tenant, and the caller acts at "Admin" level
// WITHIN that org (never Owner, so a key can't manage keys or create companies).
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IApiKeyRepository _keys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyRepository keys)
        : base(options, logger, encoder)
    {
        _keys = keys;
    }

    // What a FAH key may do: read across the app, and the two write areas FAH
    // offers (employees, payroll). Never sso:write — FAH runs inside the app.
    // The person's own role and limits narrow it further on every request.
    public static readonly IReadOnlyList<string> ActingScopes =
    [
        "employees:read", "employees:write",
        "claims:read", "leave:read", "attendance:read", "overtime:read",
        "projects:read", "teams:read", "accounts:read", "policies:read",
        "organizations:read", "documents:read",
        "payroll:read", "payroll:write",
    ];

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. Pull the raw token from "Authorization: Bearer wp_live_..." (Bearer optional).
        var header = Request.Headers.Authorization.ToString();
        var raw = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : header.Trim();

        // Not a wp_live_ token → NoResult so the request can fall through cleanly.
        if (string.IsNullOrEmpty(raw) || !raw.StartsWith(ApiTokenGenerator.Prefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        // 2. Hash it and look the key up (across all orgs — see repo).
        var key = await _keys.GetByHashAsync(ApiTokenGenerator.HashToken(raw));

        // 3. Reject unknown or revoked keys. Deliberately vague — don't reveal which.
        if (key is null || !key.Active)
            return AuthenticateResult.Fail("Invalid or revoked API key.");
        if (key.ExpiresAt is { } expires && expires <= DateTime.UtcNow)
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        if (key.ActingUserId is { } actingUserId)
            return await ActingAsync(key, actingUserId);

        // 4. Build the identity. sub is a synthetic machine id; org drives the tenant
        //    filter; Role=Admin clears the existing role-gated data endpoints.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, $"apikey:{key.Id}"),
            new("sub", $"apikey:{key.Id}"),
            new("org", key.OrganizationId),
            new(ClaimTypes.Role, "Admin"),
            new(ApiKeyAuthenticationDefaults.ApiKeyIdClaim, key.Id),
        };
        foreach (var scope in ApiScopes.Split(key.Scopes))
            claims.Add(new Claim(ApiKeyAuthenticationDefaults.ScopeClaim, scope));

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationDefaults.Scheme);
        return AuthenticateResult.Success(ticket);
    }

    // A FAH key acts as the admin it was minted for, so everything keyed on the
    // user — module access, a limited admin's people, the activity log — is
    // theirs. Re-checked on every request: removed from the company or no
    // longer an admin, and the key stops working at once.
    //
    // Always the Admin role, even for an Owner: an Owner keeps full access
    // (that comes from their membership), but owner-only actions — company
    // settings and the like — stay with a person on a screen. It still counts
    // as a key (apikey_id), so [HumanOnly] routes refuse it too.
    private async Task<AuthenticateResult> ActingAsync(ApiKey key, string actingUserId)
    {
        var member = await _keys.GetActingMemberAsync(key.OrganizationId, actingUserId);
        if (member is null || !OrgRoles.IsAdministrative(member.Role))
            return AuthenticateResult.Fail("Invalid or revoked API key.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, actingUserId),
            new("sub", actingUserId),
            new(ClaimTypes.Email, member.Email),
            new("org", key.OrganizationId),
            new(ClaimTypes.Role, OrgRoles.Admin),
            new(ApiKeyAuthenticationDefaults.ApiKeyIdClaim, key.Id),
            new(ApiKeyAuthenticationDefaults.ActingClaim, "1"),
        };
        foreach (var scope in ApiScopes.Split(key.Scopes))
            claims.Add(new Claim(ApiKeyAuthenticationDefaults.ScopeClaim, scope));

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme);
        return AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthenticationDefaults.Scheme));
    }
}
