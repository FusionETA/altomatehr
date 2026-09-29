using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Modules.Provisioning;

// A company made through POST /admin/organizations has nobody in it. The
// integration (New-Altomate, on a user's first "Open in Payroll App") then
// grants its user a seat here.
//
// The FIRST seat is Owner, every later one Admin. Before this every seat was
// Admin, so an API-made company could have no Owner at all — and the
// Owner-only screens (managing the other admins, the plan) were closed to
// everyone in it. A company that already has an Owner or an Admin keeps its
// existing people exactly as they are; only its newcomers are Admins.
public class OrgAdminService : IOrgAdminService
{
    private readonly IUserRepository _users;
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IDirectoryService _directory;

    public OrgAdminService(
        IUserRepository users,
        IOrganizationMembershipRepository memberships,
        IDirectoryService directory)
    {
        _users = users;
        _memberships = memberships;
        _directory = directory;
    }

    public async Task<GrantAdminResult> GrantAsync(string organizationId, string email, string? name)
    {
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
                Name = NameFor(name, email),
                PasswordHash = BC.HashPassword(Guid.NewGuid().ToString("N")),
                CreatedAt = DateTime.UtcNow,
            };
            await _users.AddAsync(user);
            created = true;
        }

        var membership = await _directory.GetMembershipAsync(organizationId, user.Id);
        if (membership is not null && OrgRoles.IsAdministrative(membership.Role))
        {
            // Already an administrator: a harmless no-op, which is what lets the
            // caller retry a failed SSO mint without checking first.
            return new GrantAdminResult(user.Email, created, Linked: false, membership.Role);
        }

        var role = await HasAdministratorAsync(organizationId) ? OrgRoles.Admin : OrgRoles.Owner;

        if (membership is null)
        {
            await _memberships.AddAsync(new OrganizationMembership
            {
                OrganizationId = organizationId,
                UserId = user.Id,
                Role = role,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            // An existing employee being promoted. The seat changes; the
            // account and its history do not.
            membership.Role = role;
            membership.UpdatedAt = DateTime.UtcNow;
            await _memberships.UpdateAsync(membership);
        }

        return new GrantAdminResult(user.Email, created, Linked: true, role);
    }

    public async Task<IReadOnlyList<OrgAdminRow>> ListAsync(IReadOnlySet<string> roles, int? limit)
    {
        var users = (await _directory.GetUsersAsync()).ToDictionary(u => u.Id);

        return
        [
            .. (await _directory.GetMembershipsForCurrentOrgAsync())
                .Where(m => OrgRoles.IsAdministrative(m.Role))
                .Where(m => roles.Count == 0 || roles.Contains(m.Role))
                .Where(m => users.ContainsKey(m.UserId))
                .Select(m => new OrgAdminRow(m.UserId, users[m.UserId].Email, users[m.UserId].Name, m.Role))
                .OrderBy(r => r.Email, StringComparer.OrdinalIgnoreCase)
                .Take(limit is > 0 ? limit.Value : int.MaxValue),
        ];
    }

    // Anyone administering the company already — Owner or Admin. The org key
    // scopes the membership read to this one company.
    private async Task<bool> HasAdministratorAsync(string organizationId) =>
        (await _directory.GetMembershipsForCurrentOrgAsync())
            .Any(m => m.OrganizationId == organizationId && OrgRoles.IsAdministrative(m.Role));

    // Falls back to the email local part, so a name is never empty.
    private static string NameFor(string? name, string email)
    {
        var trimmed = name?.Trim();
        if (!string.IsNullOrEmpty(trimmed)) return trimmed[..Math.Min(trimmed.Length, 120)];

        var local = email.Split('@')[0];
        return local.Length == 0 ? "Admin" : local[..Math.Min(local.Length, 120)];
    }
}
