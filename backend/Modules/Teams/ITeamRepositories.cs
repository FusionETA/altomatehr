using AltomateHR.Api.Modules.Teams.Entities;

namespace AltomateHR.Api.Modules.Teams;

public interface ITeamRepository
{
    Task<List<Team>> GetAllAsync();
    Task<Team?> GetByIdAsync(string id);
    Task<Team?> GetByProjectAndNameAsync(string projectId, string name);
    Task<Team> AddAsync(Team team);
    Task UpdateAsync(Team team);
    Task DeleteAsync(string id);
}

public interface ITeamMembershipRepository
{
    Task<List<TeamMembership>> GetAllAsync();
    Task<List<TeamMembership>> GetByTeamAsync(string teamId);
    Task<List<TeamMembership>> GetByEmployeeAsync(string employeeId);
    Task<TeamMembership?> GetByTeamAndEmployeeAsync(string teamId, string employeeId);
    Task AddAsync(TeamMembership membership);
    Task UpdateAsync(TeamMembership membership);
    Task DeleteAsync(string id);
    Task DeleteByTeamAsync(string teamId);
}
