using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Modules.Employees;

// Public read surface over the supervisor relationship, so other modules
// (leave, claims, …) can route approvals without touching the repositories
// directly. The supervisor is per active-org membership — someone can supervise
// in one org and be a plain employee in another.
public interface ISupervisionService
{
    Task<string?> GetSupervisorIdAsync(string employeeId);
    Task<IReadOnlyList<string>> GetReportIdsAsync(string supervisorId);

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

    // True when `approverId`/`role` may act on `applicantId`'s request:
    // an org approver, or the applicant's directly-assigned supervisor.
    Task<bool> CanApproveAsync(string applicantId, string approverId, string? role);
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

    // The supervisor assigned to this employee IN THE ACTIVE ORG.
    public async Task<string?> GetSupervisorIdAsync(string employeeId) =>
        (await _memberships.GetForUserInCurrentOrgAsync(employeeId))?.SupervisorId;

    // Everyone in the active org whose assigned supervisor is this person.
    public async Task<IReadOnlyList<string>> GetReportIdsAsync(string supervisorId) =>
        (await _memberships.GetBySupervisorAsync(supervisorId)).Select(m => m.UserId).ToList();

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

    public async Task<bool> CanApproveAsync(string applicantId, string approverId, string? role)
    {
        if (IsOrgApprover(role)) return true;
        var supervisorId = await GetSupervisorIdAsync(applicantId);
        return supervisorId is not null && supervisorId == approverId;
    }
}
