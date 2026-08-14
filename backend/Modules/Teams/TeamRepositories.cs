using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Teams.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Teams;

public class TeamRepository : ITeamRepository
{
    private readonly AppDbContext _db;

    public TeamRepository(AppDbContext db) => _db = db;

    public Task<List<Team>> GetAllAsync() =>
        _db.Teams.OrderBy(t => t.Name).ToListAsync();

    public Task<Team?> GetByIdAsync(string id) =>
        _db.Teams.FirstOrDefaultAsync(t => t.Id == id);

    public Task<Team?> GetByProjectAndNameAsync(string projectId, string name) =>
        _db.Teams.FirstOrDefaultAsync(t => t.ProjectId == projectId && t.Name == name);

    public async Task<Team> AddAsync(Team team)
    {
        _db.Teams.Add(team);
        await _db.SaveChangesAsync();
        return team;
    }

    public async Task UpdateAsync(Team team)
    {
        _db.Teams.Update(team);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        var team = await _db.Teams.FirstOrDefaultAsync(t => t.Id == id);
        if (team is null) return;
        _db.Teams.Remove(team);
        await _db.SaveChangesAsync();
    }
}

public class TeamMembershipRepository : ITeamMembershipRepository
{
    private readonly AppDbContext _db;

    public TeamMembershipRepository(AppDbContext db) => _db = db;

    public Task<List<TeamMembership>> GetAllAsync() =>
        _db.TeamMemberships.ToListAsync();

    public Task<List<TeamMembership>> GetByTeamAsync(string teamId) =>
        _db.TeamMemberships.Where(m => m.TeamId == teamId).ToListAsync();

    public Task<List<TeamMembership>> GetByEmployeeAsync(string employeeId) =>
        _db.TeamMemberships.Where(m => m.EmployeeId == employeeId).ToListAsync();

    public Task<TeamMembership?> GetByTeamAndEmployeeAsync(string teamId, string employeeId) =>
        _db.TeamMemberships.FirstOrDefaultAsync(m => m.TeamId == teamId && m.EmployeeId == employeeId);

    public async Task AddAsync(TeamMembership membership)
    {
        _db.TeamMemberships.Add(membership);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(TeamMembership membership)
    {
        _db.TeamMemberships.Update(membership);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        var membership = await _db.TeamMemberships.FirstOrDefaultAsync(m => m.Id == id);
        if (membership is null) return;
        _db.TeamMemberships.Remove(membership);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteByTeamAsync(string teamId)
    {
        var rows = await _db.TeamMemberships.Where(m => m.TeamId == teamId).ToListAsync();
        if (rows.Count == 0) return;
        _db.TeamMemberships.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }
}
