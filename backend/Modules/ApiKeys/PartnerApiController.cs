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
    //
    // Answers a MASTER key too. It is not an auth scheme, so it arrives
    // unauthenticated and [Authorize] used to 401 it — which left the one
    // credential that cannot read any company unable to ask what the
    // deployment supports, the exact diagnosability problem this endpoint
    // exists to prevent. A master caller is told the FEATURES and nothing
    // about any company, because it belongs to none.
    //
    // Limited, because answering a master key means looking one up before the
    // authentication re-assertion below: without a bound, 200-vs-401 here
    // tells a guesser whether a master token is real, at whatever rate they
    // like. (Guessing 32 random bytes is infeasible — this is a seatbelt, and
    // it matches the other three master-accepting endpoints.)
    [AllowAnonymous]
    [EnableRateLimiting("partner-access")]
    [HttpGet("whoami")]
    public async Task<IActionResult> WhoAmI()
    {
        if (await CallerAsync() is { IsMaster: true })
        {
            return Ok(new
            {
                kind = "master",
                organizationId = (string?)null,
                apiKeyId = (string?)null,
                tokenName = (string?)null,
                role = (string?)null,
                scopes = Array.Empty<string>(),
                features = ApiFeatures.All,
            });
        }

        // [AllowAnonymous] above turns off the gate for everyone, so the
        // company-key and signed-in-human paths re-assert it here.
        if (User.Identity?.IsAuthenticated != true)
            return Unauthorized(new { message = "Invalid or revoked API key." });

        var scopes = User.FindAll(ApiKeyAuthenticationDefaults.ScopeClaim)
            .Select(c => c.Value)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        var apiKeyId = User.FindFirst(ApiKeyAuthenticationDefaults.ApiKeyIdClaim)?.Value;

        return Ok(new
        {
            // "key" for a wp_live_ company key, "user" for a signed-in human,
            // "master" above — so a caller never has to infer it from which
            // fields came back null.
            kind = apiKeyId is null ? "user" : "key",
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

        PartnerIdentity? identity;
        try
        {
            identity = await _identity.VerifyAsync(caller, dto.Email ?? string.Empty, dto.Password ?? string.Empty);
        }
        catch (Auth.LoginBlockedException ex)
        {
            // The account is locked (LoginGuard). Locks are kept for unknown
            // emails too, so saying so tells the key holder nothing new.
            Response.Headers.RetryAfter = ((int)Math.Ceiling(Math.Max(1, ex.RetryAfter.TotalSeconds))).ToString();
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = ex.Message });
        }

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
    //
    // On the partner limit, not the login one: there is no password here to
    // guess, and this is a companion's "refresh" — one call per sign-in and
    // per refresh from ONE server address, where 5/min (shared with
    // /auth/login and /auth/verify) throttled an ordinary afternoon.
    [AllowAnonymous]
    [EnableRateLimiting("partner-access")]
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

    // POST /auth/access — what ONE person may reach in ONE company.
    //
    // The gap this closes: a companion app holds the company's wp_live_ key,
    // which sees the WHOLE company. Nothing in that key says the signed-in
    // admin's Manage access is limited to some employees, so the companion
    // would show them everyone's pay. It asks here instead, and filters.
    //
    // MASTER KEY ONLY. A company key may already read its own company; being
    // able to ask what another PERSON may reach would make it a directory of
    // who is restricted, which is not what the company granted it.
    //
    // Read live, never cached: an Owner who narrows an admin at 10:00 expects
    // the companion to honour it at 10:01. Call it at sign-in, on refresh, and
    // again immediately before posting a payroll run.
    [AllowAnonymous]
    [EnableRateLimiting("partner-access")]
    [HttpPost("auth/access")]
    public async Task<IActionResult> Access(LookupAccessDto dto)
    {
        var caller = await CallerAsync();
        if (caller is null)
            return Unauthorized(new { message = "Invalid or revoked API key." });

        if (!caller.IsMaster)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "This endpoint needs the platform master key." });

        // Checked here rather than with [Required], so an unauthenticated
        // caller gets "invalid key" and not a description of the body.
        if (string.IsNullOrWhiteSpace(dto.UserId) || string.IsNullOrWhiteSpace(dto.OrganizationId))
            return BadRequest(new { message = "Send a userId and an organizationId." });

        var access = await _identity.AccessAsync(caller, dto.UserId.Trim(), dto.OrganizationId.Trim());

        // One answer for an unknown user, someone who is not an admin here,
        // and a deactivated company — the same reason /auth/verify gives one.
        if (access is null)
            return NotFound(new { message = "No such user, or no access to that organization." });

        var body = new Dictionary<string, object?>
        {
            ["userId"] = access.UserId,
            ["organizationId"] = access.OrganizationId,
            ["role"] = access.Role,
            ["allEmployees"] = access.AllEmployees,
            ["canRunPayroll"] = access.CanRunPayroll,
            ["canChangeSettings"] = access.CanChangeSettings,
            ["modules"] = access.Modules,
        };

        // OMITTED rather than null when there is no restriction, the same way
        // /pending omits a section it cannot answer: "everyone" and "these
        // people" are different answers, and an empty list would be a third.
        if (access.Employees is not null) body["employees"] = access.Employees;

        return Ok(body);
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

public class LookupAccessDto
{
    // Both required, but checked in the action rather than here: model
    // validation runs BEFORE the key check, so [Required] would describe the
    // body to a caller holding no credential at all.
    public string? UserId { get; set; }

    public string? OrganizationId { get; set; }
}

public class LookupOrganizationsDto
{
    // Either one. userId is what a companion that already verified someone holds.
    public string? UserId { get; set; }

    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? Email { get; set; }
}
