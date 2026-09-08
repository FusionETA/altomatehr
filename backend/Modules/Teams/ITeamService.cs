using AltomateHR.Api.Modules.Teams.Dtos;

namespace AltomateHR.Api.Modules.Teams;

public interface ITeamService
{
    Task<IEnumerable<TeamDto>> GetAllAsync();
    Task<TeamSaveResult> CreateAsync(CreateTeamDto dto);
    Task<TeamSaveResult> UpdateAsync(string id, SaveTeamDto dto);
    Task<bool> DeleteAsync(string id);
    Task<TeamSaveResult> AddOrUpdateMemberAsync(string teamId, SaveMembershipDto dto);
    Task<TeamSaveResult> RemoveMemberAsync(string teamId, string employeeId);

    // `projectId` disambiguates which of the employee's teams governs when
    // they're on more than one — see ApprovalChainService.GetChainAsync.
    Task<IEnumerable<ApprovalStepDto>> GetApprovalChainAsync(
        string employeeId, ApprovalModule module, string? projectId = null);

    // The employee ids on a team — enough for callers that only filter by
    // membership, without handing out the membership rows themselves.
    Task<IReadOnlyList<string>> GetMemberEmployeeIdsAsync(string teamId);

    // Every team the caller oversees, each with the members below them and the
    // project it belongs to. Empty for someone at the bottom layer everywhere.
    Task<IReadOnlyList<SupervisedTeamDto>> GetSupervisedTeamsAsync(string userId);

    // Every employee below the caller across ANY team they supervise, flattened
    // to one list — the Team-based replacement for the old flat
    // OrganizationMembership.SupervisorId "reports" concept. Used for "may this
    // person view that employee's data" checks, not approval routing.
    Task<IReadOnlyList<string>> GetReportEmployeeIdsAsync(string supervisorId);

    // The projects this employee is on, via their team memberships. Empty means
    // they're on no project — not that they're on all of them.
    Task<IReadOnlyList<string>> GetProjectIdsForMemberAsync(string employeeId);

    // Per-layer approver picker data for one employee's team membership: the
    // candidate pool, what's currently in effect, and whether it's a custom
    // override or the team's implicit default. Null → not a member of this team.
    Task<IReadOnlyList<LayerApproverOptionsDto>?> GetApproverOptionsAsync(string teamId, string employeeId);

    // Set/replace the explicit approver list for one employee at one layer.
    // An empty list is a deliberate "nobody approves here for this person",
    // distinct from never having set an override at all.
    Task<ApproverOverrideResult> SetApproverOverrideAsync(
        string teamId, string employeeId, int layer, List<string> approverIds);

    // Remove the override, reverting that layer to the team's implicit default.
    Task<ApproverOverrideResult> ClearApproverOverrideAsync(string teamId, string employeeId, int layer);
}

// Ok=false with Error → 400; Ok=false and Error null → not found (404).
public record TeamSaveResult(bool Ok, TeamDto? Team, string? Error);

public record ApproverOverrideResult(bool Ok, IReadOnlyList<LayerApproverOptionsDto>? Options, string? Error);
