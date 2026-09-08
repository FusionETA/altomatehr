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

    public bool IsOrgApprover(string? role) => OrgRoles.IsAdministrative(role);

    public async Task<IReadOnlySet<string>> GetAdministrativeUserIdsAsync() =>
        (await _directory.GetMembershipsForCurrentOrgAsync())
            .Where(m => OrgRoles.IsAdministrative(m.Role))
            .Select(m => m.UserId)
            .ToHashSet();
}
