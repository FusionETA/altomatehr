using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Leave;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AltomateHR.Api.Modules.Auth;

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
    private readonly IAuthService _auth;

    public PartnerApiController(
        IDirectoryService directory, IClaimsService claims, ILeaveService leave, IAuthService auth)
    {
        _directory = directory;
        _claims = claims;
        _leave = leave;
        _auth = auth;
    }

    // GET /whoami — what this credential is and what it may do.
    //
    // No scope required: a caller holding the key is entitled to know what the
    // key is, and refusing would make a key impossible to diagnose.
    [HttpGet("whoami")]
    public IActionResult WhoAmI()
    {
        var scopes = User.FindAll(ApiKeyAuthenticationDefaults.ScopeClaim)
            .Select(c => c.Value)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();

        return Ok(new
        {
            organizationId = User.FindFirst("org")?.Value,
            // Present for a wp_live_ key, absent for a signed-in human — which
            // is itself the answer to "what kind of caller am I".
            apiKeyId = User.FindFirst(ApiKeyAuthenticationDefaults.ApiKeyIdClaim)?.Value,
            role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
            scopes,
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
    // that wants to authenticate someone before handing them over.
    //
    // Rate limited, because this is a password oracle for anyone holding a key:
    // without a limit it is an offline-speed guessing machine against real
    // accounts. Same policy the login endpoint uses.
    //
    // Admins and owners only, matching the SSO hand-off it exists to precede.
    [EnableRateLimiting("auth-login")]
    [HttpPost("auth/verify")]
    public async Task<IActionResult> Verify(VerifyCredentialsDto dto)
    {
        var organizationId = User.FindFirst("org")?.Value;
        if (string.IsNullOrEmpty(organizationId))
            return Unauthorized(new { message = "This credential is not scoped to an organization." });

        var result = await _auth.LoginAsync(dto.Email ?? string.Empty, dto.Password ?? string.Empty);

        // One answer for a wrong password, an unknown email, a non-admin, and
        // someone who administers a different company. Anything finer tells a
        // key holder which emails exist.
        var membership = result is null || !OrgRoles.IsAdministrative(result.Role)
            ? null
            : await MembershipForEmailAsync(organizationId, result.Email);

        if (membership is null)
            return Unauthorized(new { message = "Invalid credentials for this organization." });

        return Ok(new { email = result.Email, role = result.Role, organizationId });
    }

    // POST /auth/organizations — which organizations an account administers.
    //
    // Scoped to what the CALLING KEY can see: an integration asking about a
    // person gets back only the org its own key belongs to, never that person's
    // other employers.
    [HttpPost("auth/organizations")]
    public async Task<IActionResult> Organizations(LookupOrganizationsDto dto)
    {
        var organizationId = User.FindFirst("org")?.Value;
        if (string.IsNullOrEmpty(organizationId))
            return Unauthorized(new { message = "This credential is not scoped to an organization." });

        var users = await _directory.GetUsersAsync();
        var user = users.FirstOrDefault(u =>
            string.Equals(u.Email, (dto.Email ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

        if (user is null) return Ok(new { organizations = Array.Empty<object>() });

        var membership = await _directory.GetMembershipAsync(organizationId, user.Id);
        var organizations = membership is not null && OrgRoles.IsAdministrative(membership.Role)
            ? new[] { new { organizationId, role = membership.Role } }
            : [];

        return Ok(new { organizations });
    }

    // AuthResult carries the email, not the id, and memberships key on the id —
    // so it is resolved here rather than widening AuthResult for one caller.
    private async Task<Employees.Entities.OrganizationMembership?> MembershipForEmailAsync(
        string organizationId, string email)
    {
        var users = await _directory.GetUsersAsync();
        var user = users.FirstOrDefault(u =>
            string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

        return user is null ? null : await _directory.GetMembershipAsync(organizationId, user.Id);
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
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? Email { get; set; }
}
