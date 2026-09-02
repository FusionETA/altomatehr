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
}

// Ok=false with Error → 400; Ok=false and Error null → not found (404).
public record TeamSaveResult(bool Ok, TeamDto? Team, string? Error);
