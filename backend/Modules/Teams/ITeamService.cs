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
    Task<IEnumerable<ApprovalStepDto>> GetApprovalChainAsync(string employeeId, ApprovalModule module);

    // The employee ids on a team — enough for callers that only filter by
    // membership, without handing out the membership rows themselves.
    Task<IReadOnlyList<string>> GetMemberEmployeeIdsAsync(string teamId);

    // Every team the caller oversees, each with the members below them and the
    // project it belongs to. Empty for someone at the bottom layer everywhere.
    Task<IReadOnlyList<SupervisedTeamDto>> GetSupervisedTeamsAsync(string userId);

    // The projects this employee is on, via their team memberships. Empty means
    // they're on no project — not that they're on all of them.
    Task<IReadOnlyList<string>> GetProjectIdsForMemberAsync(string employeeId);
}

// Ok=false with Error → 400; Ok=false and Error null → not found (404).
public record TeamSaveResult(bool Ok, TeamDto? Team, string? Error);
