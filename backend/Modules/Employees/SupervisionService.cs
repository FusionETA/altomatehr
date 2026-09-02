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

    // True when `role` may approve any request in the org (admin/owner override).
    bool IsOrgApprover(string? role);

    // True when `approverId`/`role` may act on `applicantId`'s request:
    // an org approver, or the applicant's directly-assigned supervisor.
    Task<bool> CanApproveAsync(string applicantId, string approverId, string? role);
}

public class SupervisionService : ISupervisionService
{
    private static readonly string[] OrgApproverRoles = ["Admin", "Owner"];

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

    public bool IsOrgApprover(string? role) =>
        role is not null && OrgApproverRoles.Contains(role, StringComparer.OrdinalIgnoreCase);

    public async Task<bool> CanApproveAsync(string applicantId, string approverId, string? role)
    {
        if (IsOrgApprover(role)) return true;
        var supervisorId = await GetSupervisorIdAsync(applicantId);
        return supervisorId is not null && supervisorId == approverId;
    }
}
