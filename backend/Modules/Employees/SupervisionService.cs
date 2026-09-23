using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Modules.Employees;

// Public read surface over the supervisor relationship, so other modules
// (leave, claims, …) can route approvals without touching the repositories
// directly. The supervisor is per active-org membership — someone can supervise
// in one org and be a plain employee in another.
public interface ISupervisionService
{
    // Email lookup so approver views can label a request by who filed it.
    Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IEnumerable<string> userIds);

    // The person's real name, for the same views. Only people who HAVE one are
    // in the result: a blank name is left for the client to fall back to the
    // email, rather than sent as "" and shown as nobody.
    Task<IReadOnlyDictionary<string, string>> GetNamesAsync(IEnumerable<string> userIds);

    // True when `role` is an administrative (Admin/Owner) seat.
    //
    // Named for approval for historical reasons, but every caller uses it as a
    // VISIBILITY check — "may this person see another employee's data" — which
    // is what an oversight seat is for. It grants no power to decide a request:
    // approval routing goes through IApprovalRouter, which excludes these roles
    // outright. See OrgRoles.
    bool IsOrgApprover(string? role);

    // Everyone in the current org holding an administrative seat. Approval
    // routing subtracts these, so an admin sitting in a team never becomes
    // somebody's approver.
    Task<IReadOnlySet<string>> GetAdministrativeUserIdsAsync();

    // This person's role in the current org, or null when they aren't a member
    // here. Teams needs it to refuse putting a plain Employee in a layer that
    // would make them somebody's approver.
    Task<string?> GetRoleAsync(string userId);
}

public class SupervisionService : ISupervisionService
{
    private readonly IDirectoryService _directory;
    private readonly IOrganizationMembershipRepository _memberships;

    public SupervisionService(IOrganizationMembershipRepository memberships, IDirectoryService directory)
    {
        _memberships = memberships;
        _directory = directory;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IEnumerable<string> userIds)
    {
        var wanted = userIds.ToHashSet();
        if (wanted.Count == 0) return new Dictionary<string, string>();
        var users = await _directory.GetUsersAsync();
        return users.Where(u => wanted.Contains(u.Id)).ToDictionary(u => u.Id, u => u.Email);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetNamesAsync(IEnumerable<string> userIds)
    {
        var wanted = userIds.ToHashSet();
        if (wanted.Count == 0) return new Dictionary<string, string>();
        var users = await _directory.GetUsersAsync();
        return users
            .Where(u => wanted.Contains(u.Id) && !string.IsNullOrWhiteSpace(u.Name))
            .ToDictionary(u => u.Id, u => u.Name.Trim());
    }

    public bool IsOrgApprover(string? role) => OrgRoles.IsAdministrative(role);

    public async Task<IReadOnlySet<string>> GetAdministrativeUserIdsAsync() =>
        (await _directory.GetMembershipsForCurrentOrgAsync())
            .Where(m => OrgRoles.IsAdministrative(m.Role))
            .Select(m => m.UserId)
            .ToHashSet();

    public async Task<string?> GetRoleAsync(string userId) =>
        (await _directory.GetMembershipForUserAsync(userId))?.Role;
}
