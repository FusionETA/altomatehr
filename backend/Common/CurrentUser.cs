using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Common;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    private readonly CompanyPrincipal? _company;

    public CurrentUser(IHttpContextAccessor http, CompanyPrincipal? company = null)
    {
        _http = http;
        _company = company;
    }

    // Inside an ICompanyScopes scope, the person as they are in that company;
    // otherwise the request's own token.
    private ClaimsPrincipal? Principal => _company?.Principal ?? _http.HttpContext?.User;

    public string? UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);

    public string? OrganizationId => Principal?.FindFirstValue("org");

    public string? Email =>
        Principal?.FindFirstValue(ClaimTypes.Email)
        ?? Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);

    public bool IsAdmin => Principal?.IsInRole("Admin") ?? false;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsSupport => Principal?.FindFirstValue("support") == "1";

    public bool IsSso => Principal?.FindFirstValue("sso") == "1";

    public bool IsFormer => Principal?.FindFirstValue("former") == "1";

    // Same test as [RequireScope]: a wp_live_ key carries apikey_id, a partner
    // app partner_client (PartnerAuthenticationDefaults.ClientIdClaim, kept as
    // a literal so Common doesn't depend on Partners). Both hold their granted
    // scopes as ScopeClaim.
    public bool IsScopedMachine =>
        Principal is { } p
        && (p.HasClaim(c => c.Type == ApiKeyAuthenticationDefaults.ApiKeyIdClaim)
            || p.HasClaim(c => c.Type == "partner_client"));

    public bool HasScope(string scope) =>
        !IsScopedMachine || Principal!.HasClaim(ApiKeyAuthenticationDefaults.ScopeClaim, scope);

    // The caller's real remote IP (attendance allowlist, login limits) — see ClientIp.
    public string? IpAddress => ClientIp.Of(_http.HttpContext);
}
