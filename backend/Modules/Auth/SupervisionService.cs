namespace AltomateHR.Api.Modules.Auth;

// Public read surface over the supervisor relationship, so other modules
// (leave, claims, …) can route approvals without touching the user repository
// directly. Roles that approve anything in the org, regardless of assignment.
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

    private readonly IUserRepository _users;

    public SupervisionService(IUserRepository users) => _users = users;

    public async Task<string?> GetSupervisorIdAsync(string employeeId) =>
        (await _users.GetByIdAsync(employeeId))?.SupervisorId;

    public async Task<IReadOnlyList<string>> GetReportIdsAsync(string supervisorId) =>
        (await _users.GetBySupervisorAsync(supervisorId)).Select(u => u.Id).ToList();

    public async Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IEnumerable<string> userIds)
    {
        var wanted = userIds.ToHashSet();
        if (wanted.Count == 0) return new Dictionary<string, string>();
        var users = await _users.GetAllAsync();
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
