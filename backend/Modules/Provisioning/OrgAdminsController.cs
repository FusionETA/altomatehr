using AltomateHR.Api.Modules.ApiKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Provisioning;

// Granting admin on the calling key's OWN organization.
//
// Authenticated by the per-org wp_live_ key, NOT the master key: the key scopes
// the grant, so a caller can only ever make an admin of the company it already
// integrates with.
//
// This is the step the SSO mint deliberately does not do for you. An external
// platform whose hand-off is refused because the email is not an admin yet
// calls this explicitly, then retries. Keeping it a separate, named call is the
// whole point — silently minting an admin as a side effect of an SSO redirect
// is trust nobody asked for, whereas this is an integration stating plainly
// what it wants.
[ApiController]
[Route("admin")]
[Authorize]
public class OrgAdminsController : ControllerBase
{
    private readonly IOrgAdminService _admins;

    public OrgAdminsController(IOrgAdminService admins) => _admins = admins;

    // POST /admin/admins — make this email an administrator of the calling
    // key's org. The first person given a seat in a company with nobody
    // administering it becomes its Owner; everyone after is an Admin.
    //
    // Idempotent: `created` says whether a new account was made, `linked`
    // whether an existing one gained the seat, `role` which seat they hold.
    // Re-sending for someone who is already an admin is a harmless no-op, which
    // is what lets the caller retry a failed SSO mint without checking first.
    [RequireScope("organizations:write")]
    [HttpPost("admins")]
    public async Task<IActionResult> GrantAdmin(GrantAdminRequest request)
    {
        var organizationId = User.FindFirst("org")?.Value;
        if (string.IsNullOrEmpty(organizationId))
            return Unauthorized(new { message = "This credential is not scoped to an organization." });

        var email = (request.Email ?? string.Empty).Trim();
        if (email.Length == 0) return BadRequest(new { message = "Email is required." });

        var result = await _admins.GrantAsync(organizationId, email, request.Name);
        return Ok(new { email = result.Email, created = result.Created, linked = result.Linked, role = result.Role });
    }

    // GET /admin/users?role=OWNER,ADMIN&limit=1 — who administers this org.
    //
    // The role filter is accepted for compatibility with callers that send it;
    // this endpoint only ever returns administrative seats, because that is the
    // only question it is asked.
    [RequireScope("organizations:read")]
    [HttpGet("users")]
    public async Task<IActionResult> ListAdmins([FromQuery] string? role, [FromQuery] int? limit)
    {
        var rows = (await _admins.ListAsync(ParseRoles(role), limit))
            .Select(r => new { userId = r.UserId, email = r.Email, name = r.Name, role = r.Role })
            .ToList();

        return Ok(new { data = rows, total = rows.Count });
    }

    // "OWNER,ADMIN" — matched case-insensitively against this app's own casing,
    // since the caller's vocabulary is upper-case and ours is not.
    private static HashSet<string> ParseRoles(string? role)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in (role ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0) wanted.Add(trimmed);
        }
        return wanted;
    }
}

public class GrantAdminRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? Email { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(120)]
    public string? Name { get; set; }
}
