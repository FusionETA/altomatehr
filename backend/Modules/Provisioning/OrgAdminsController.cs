using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BC = BCrypt.Net.BCrypt;

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
    private readonly IUserRepository _users;
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IDirectoryService _directory;

    public OrgAdminsController(
        IUserRepository users,
        IOrganizationMembershipRepository memberships,
        IDirectoryService directory)
    {
        _users = users;
        _memberships = memberships;
        _directory = directory;
    }

    // POST /admin/admins — make this email an admin of the calling key's org.
    //
    // Idempotent: `created` says whether a new account was made, `linked`
    // whether an existing one gained the seat. Re-sending for someone who is
    // already an admin is a harmless no-op, which is what lets the caller retry
    // a failed SSO mint without checking first.
    [RequireScope("organizations:write")]
    [HttpPost("admins")]
    public async Task<IActionResult> GrantAdmin(GrantAdminRequest request)
    {
        var organizationId = User.FindFirst("org")?.Value;
        if (string.IsNullOrEmpty(organizationId))
            return Unauthorized(new { message = "This credential is not scoped to an organization." });

        var email = (request.Email ?? string.Empty).Trim();
        if (email.Length == 0) return BadRequest(new { message = "Email is required." });

        var user = await _users.GetByEmailAsync(email);
        var created = false;

        if (user is null)
        {
            // No password is set, and none is asked for. This account exists to
            // be entered through the SSO hand-off, so a password would be a
            // second way in that nobody chose and nobody would rotate.
            // BCrypt of a random secret, so the column is never a usable blank.
            user = new User
            {
                Email = email,
                Name = NameFor(request.Name, email),
                PasswordHash = BC.HashPassword(Guid.NewGuid().ToString("N")),
                CreatedAt = DateTime.UtcNow,
            };
            await _users.AddAsync(user);
            created = true;
        }

        var membership = await _directory.GetMembershipAsync(organizationId, user.Id);
        var linked = false;

        if (membership is null)
        {
            await _memberships.AddAsync(new OrganizationMembership
            {
                OrganizationId = organizationId,
                UserId = user.Id,
                Role = OrgRoles.Admin,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            linked = true;
        }
        else if (!OrgRoles.IsAdministrative(membership.Role))
        {
            // An existing employee being promoted. The seat changes; the
            // account and its history do not.
            membership.Role = OrgRoles.Admin;
            membership.UpdatedAt = DateTime.UtcNow;
            await _memberships.UpdateAsync(membership);
            linked = true;
        }

        return Ok(new { email = user.Email, created, linked, role = OrgRoles.Admin });
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
        var wanted = ParseRoles(role);

        var memberships = (await _directory.GetMembershipsForCurrentOrgAsync())
            .Where(m => OrgRoles.IsAdministrative(m.Role))
            .Where(m => wanted.Count == 0 || wanted.Contains(m.Role))
            .ToList();

        var users = (await _directory.GetUsersAsync()).ToDictionary(u => u.Id);

        var rows = memberships
            .Select(m => new
            {
                userId = m.UserId,
                email = users.TryGetValue(m.UserId, out var u) ? u.Email : null,
                name = users.TryGetValue(m.UserId, out var n) ? n.Name : null,
                role = m.Role,
            })
            .Where(r => r.email is not null)
            .OrderBy(r => r.email, StringComparer.OrdinalIgnoreCase)
            .Take(limit is > 0 ? limit.Value : int.MaxValue)
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

    // Falls back to the email local part, so a name is never empty.
    private static string NameFor(string? name, string email)
    {
        var trimmed = name?.Trim();
        if (!string.IsNullOrEmpty(trimmed)) return trimmed[..Math.Min(trimmed.Length, 120)];

        var local = email.Split('@')[0];
        return local.Length == 0 ? "Admin" : local[..Math.Min(local.Length, 120)];
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
