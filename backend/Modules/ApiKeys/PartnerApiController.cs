using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Leave;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AltomateHR.Api.Modules.ApiKeys;

// The small read surface an integration needs to orient itself, ported from the
// reference app's /api/v1/whoami, /employees/active-count and /pending.
//
// These are the endpoints an external platform calls to answer "is this key
// alive, what may it do, how many seats am I billing, and is anything waiting" —
// none of which the per-resource endpoints answer without fetching lists the
// caller does not want.
//
// Everything here is org-scoped by the key's own tenant, so there is no
// parameter to get wrong.
[ApiController]
[Route("")]
[Authorize]
public class PartnerApiController : ControllerBase
{
    private readonly IDirectoryService _directory;
    private readonly IClaimsService _claims;
    private readonly ILeaveService _leave;
    private readonly IPartnerIdentityService _identity;

    public PartnerApiController(
        IDirectoryService directory, IClaimsService claims, ILeaveService leave,
        IPartnerIdentityService identity)
    {
        _directory = directory;
        _claims = claims;
        _leave = leave;
        _identity = identity;
    }

    // GET /whoami — what this credential is and what it may do.
    //
    // No scope required: a caller holding the key is entitled to know what the
    // key is, and refusing would make a key impossible to diagnose.
    [HttpGet("whoami")]
    public async Task<IActionResult> WhoAmI()
    {
        var scopes = User.FindAll(ApiKeyAuthenticationDefaults.ScopeClaim)
            .Select(c => c.Value)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        var apiKeyId = User.FindFirst(ApiKeyAuthenticationDefaults.ApiKeyIdClaim)?.Value;

        return Ok(new
        {
            organizationId = User.FindFirst("org")?.Value,
            // Present for a wp_live_ key, absent for a signed-in human — which
            // is itself the answer to "what kind of caller am I".
            apiKeyId,
            // The key's own label, so an integration holding several can tell
            // which one it pasted.
            tokenName = apiKeyId is null ? null : await _identity.KeyNameAsync(apiKeyId),
            role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
            scopes,
            // The deploy gate integrations probe before sending an optional
            // block. Their schemas are strict, so an endpoint this deployment
            // does not have yet would 400 the whole call — they check here
            // instead of us coordinating release times.
            features = ApiFeatures.All,
        });
    }

    // GET /employees/active-count — seats, for billing.
    //
    // Deliberately its own endpoint rather than counting a list: the caller
    // wants one number, and an org with 231 people should not have to transfer
    // 231 records to learn it.
    [RequireScope("employees:read")]
    [HttpGet("employees/active-count")]
    public async Task<IActionResult> ActiveCount()
    {
        // Memberships, not users: the same person in two orgs is two seats, and
        // each org bills its own.
        var count = (await _directory.GetMembershipsForCurrentOrgAsync()).Count;

        return Ok(new { count, asOf = DateTime.UtcNow.ToString("O") });
    }

    // GET /pending — what is waiting for a decision, per module.
    //
    // A section the key cannot read is OMITTED rather than reported as zero:
    // "nothing pending" and "you may not ask" are different answers, and
    // conflating them would have an integration report a clean queue it simply
    // cannot see.
    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var held = User.FindAll(ApiKeyAuthenticationDefaults.ScopeClaim)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        // A signed-in human holds no scope claims and is limited by their role
        // instead, so they see everything this endpoint offers.
        var isKey = User.HasClaim(c => c.Type == ApiKeyAuthenticationDefaults.ApiKeyIdClaim);
        bool Can(string scope) => !isKey || held.Contains(scope);

        var body = new Dictionary<string, object?>();
        var omitted = new List<string>();

        if (Can("claims:read")) body["claims"] = await CountPendingClaimsAsync();
        else omitted.Add("claims");

        if (Can("leave:read")) body["leave"] = await CountPendingLeaveAsync();
        else omitted.Add("leave");

        body["omitted"] = omitted;
        return Ok(body);
    }

    private async Task<int> CountPendingClaimsAsync() =>
        (await _claims.GetAllForOrgAsync())
            .Count(c => c.Status == Claims.Entities.ClaimStatus.PENDING);

    private async Task<int> CountPendingLeaveAsync() =>
        (await _leave.GetAllForOrgAsync())
            .Count(l => l.Status == Leave.Entities.LeaveStatus.PENDING);

    // POST /auth/verify — check an email and password, for an external platform
    // that wants to authenticate someone before minting ITS OWN session. Only
    // the password is checked: no session, refresh token or login entry here.
    //
    // Accepts an org key (answers only about that company) or a MASTER key
    // (wp_master_) — a first-party companion app such as ABPay, whose owner
    // runs several companies. A master key is not an auth scheme, hence
    // [AllowAnonymous] and the explicit check; no data endpoint accepts it.
    //
    // Rate limited, because this is a password oracle for anyone holding a key:
    // without a limit it is an offline-speed guessing machine against real
    // accounts. Same policy the login endpoint uses.
    //
    // Admins and owners only, matching the SSO hand-off it exists to precede.
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("auth/verify")]
    public async Task<IActionResult> Verify(VerifyCredentialsDto dto)
    {
        var caller = await CallerAsync();
        if (caller is null)
            return Unauthorized(new { message = "Invalid or revoked API key." });

        var identity = await _identity.VerifyAsync(caller, dto.Email ?? string.Empty, dto.Password ?? string.Empty);

        // One answer for a wrong password, an unknown email, a non-admin, and
        // someone who administers a different company. Anything finer tells a
        // key holder which emails exist.
        if (identity is null)
            return Unauthorized(new { message = "Invalid credentials for this organization." });

        return Ok(new
        {
            id = identity.Id,
            name = identity.Name,
            email = identity.Email,
            role = identity.Role,
            organizationId = identity.OrganizationId,
            organizationName = identity.OrganizationName,
            organizations = identity.Organizations,
        });
    }

    // POST /auth/organizations — which companies an account administers, by
    // `userId` or `email` (no password: a companion's "refresh" button).
    //
    // Scoped to what the CALLER can see: an org key gets back only its own
    // company, never that person's other employers; a master key gets them all.
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("auth/organizations")]
    public async Task<IActionResult> Organizations(LookupOrganizationsDto dto)
    {
        var caller = await CallerAsync();
        if (caller is null)
            return Unauthorized(new { message = "Invalid or revoked API key." });

        if (string.IsNullOrWhiteSpace(dto.UserId) && string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Send a userId or an email." });

        return Ok(new { organizations = await _identity.AdminOrganizationsAsync(caller, dto.UserId, dto.Email) });
    }

    // A master key from the header, or the org of a wp_live_ key the normal
    // scheme resolved. A signed-in human's session is not an integration
    // credential, so it counts as neither.
    private Task<PartnerCaller?> CallerAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var bearer = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : header.Trim();

        var keyOrganizationId = User.HasClaim(c => c.Type == ApiKeyAuthenticationDefaults.ApiKeyIdClaim)
            ? User.FindFirst("org")?.Value
            : null;

        return _identity.ResolveCallerAsync(bearer, keyOrganizationId);
    }
}

public class VerifyCredentialsDto
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? Email { get; set; }

    [System.ComponentModel.DataAnnotations.Required]
    public string? Password { get; set; }
}

public class LookupOrganizationsDto
{
    // Either one. userId is what a companion that already verified someone holds.
    public string? UserId { get; set; }

    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? Email { get; set; }
}
