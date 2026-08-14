using System.Text.Json;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Teams.Dtos;
using AltomateHR.Api.Modules.Teams.Entities;

namespace AltomateHR.Api.Modules.Teams;

// Owns team CRUD + membership. Employee emails are resolved through the Auth
// supervision service so a team roster shows who's on it.
public class TeamService : ITeamService
{
    private readonly ITeamRepository _teams;
    private readonly ITeamMembershipRepository _memberships;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalChainService _chain;

    public TeamService(
        ITeamRepository teams,
        ITeamMembershipRepository memberships,
        ISupervisionService supervision,
        IApprovalChainService chain)
    {
        _teams = teams;
        _memberships = memberships;
        _supervision = supervision;
        _chain = chain;
    }

    public async Task<IEnumerable<ApprovalStepDto>> GetApprovalChainAsync(string employeeId, ApprovalModule module)
    {
        var chain = await _chain.GetChainAsync(employeeId, module);
        var emails = await _supervision.GetEmailsAsync(chain.SelectMany(s => s.ApproverIds).Distinct());
        return chain.Select(s => new ApprovalStepDto
        {
            Step = s.Step,
            LayerLabel = s.LayerLabel,
            Approvers = s.ApproverIds
                .Select(id => new ChainApproverDto { EmployeeId = id, Email = emails.GetValueOrDefault(id) })
                .ToList(),
        });
    }

    public async Task<IEnumerable<TeamDto>> GetAllAsync()
    {
        var teams = await _teams.GetAllAsync();
        var memberships = await _memberships.GetAllAsync();
        var emails = await _supervision.GetEmailsAsync(memberships.Select(m => m.EmployeeId).Distinct());
        var byTeam = memberships.GroupBy(m => m.TeamId).ToDictionary(g => g.Key, g => g.ToList());

        return teams.Select(t => ToDto(t, byTeam.GetValueOrDefault(t.Id, []), emails));
    }

    public async Task<TeamSaveResult> CreateAsync(CreateTeamDto dto)
    {
        var name = dto.Name.Trim();
        if (await _teams.GetByProjectAndNameAsync(dto.ProjectId, name) is not null)
            return new TeamSaveResult(false, null, $"This project already has a team named \"{name}\".");

        var now = DateTime.UtcNow;
        var team = new Team
        {
            ProjectId = dto.ProjectId,
            Name = name,
            LayerCount = dto.LayerCount,
            LayerLabels = Serialize(dto.LayerLabels),
            ModuleApprovalConfig = SerializeConfig(dto.ModuleApprovalConfig),
            CreatedAt = now,
            UpdatedAt = now,
        };
        await _teams.AddAsync(team);
        return new TeamSaveResult(true, await BuildAsync(team), null);
    }

    public async Task<TeamSaveResult> UpdateAsync(string id, SaveTeamDto dto)
    {
        var team = await _teams.GetByIdAsync(id);
        if (team is null) return new TeamSaveResult(false, null, null);

        var name = dto.Name.Trim();
        var clash = await _teams.GetByProjectAndNameAsync(team.ProjectId, name);
        if (clash is not null && clash.Id != id)
            return new TeamSaveResult(false, null, $"This project already has a team named \"{name}\".");

        team.Name = name;
        team.LayerCount = dto.LayerCount;
        team.LayerLabels = Serialize(dto.LayerLabels);
        team.ModuleApprovalConfig = SerializeConfig(dto.ModuleApprovalConfig);
        team.UpdatedAt = DateTime.UtcNow;
        await _teams.UpdateAsync(team);
        return new TeamSaveResult(true, await BuildAsync(team), null);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var team = await _teams.GetByIdAsync(id);
        if (team is null) return false;

        await _memberships.DeleteByTeamAsync(id);   // clear the roster first
        await _teams.DeleteAsync(id);
        return true;
    }

    public async Task<TeamSaveResult> AddOrUpdateMemberAsync(string teamId, SaveMembershipDto dto)
    {
        var team = await _teams.GetByIdAsync(teamId);
        if (team is null) return new TeamSaveResult(false, null, null);

        if (dto.Layer < 0 || dto.Layer >= team.LayerCount)
            return new TeamSaveResult(false, null, "That layer doesn't exist on this team.");

        // Validate the employee exists in the org (email lookup returns nothing otherwise).
        var known = await _supervision.GetEmailsAsync([dto.EmployeeId]);
        if (!known.ContainsKey(dto.EmployeeId))
            return new TeamSaveResult(false, null, "That employee doesn't exist in this organization.");

        var now = DateTime.UtcNow;
        var existing = await _memberships.GetByTeamAndEmployeeAsync(teamId, dto.EmployeeId);
        if (existing is null)
        {
            await _memberships.AddAsync(new TeamMembership
            {
                TeamId = teamId,
                EmployeeId = dto.EmployeeId,
                Layer = dto.Layer,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.Layer = dto.Layer;
            existing.UpdatedAt = now;
            await _memberships.UpdateAsync(existing);
        }

        return new TeamSaveResult(true, await BuildAsync(team), null);
    }

    public async Task<TeamSaveResult> RemoveMemberAsync(string teamId, string employeeId)
    {
        var team = await _teams.GetByIdAsync(teamId);
        if (team is null) return new TeamSaveResult(false, null, null);

        var membership = await _memberships.GetByTeamAndEmployeeAsync(teamId, employeeId);
        if (membership is not null) await _memberships.DeleteAsync(membership.Id);

        return new TeamSaveResult(true, await BuildAsync(team), null);
    }

    // Load a single team's roster + emails into a DTO.
    private async Task<TeamDto> BuildAsync(Team team)
    {
        var members = await _memberships.GetByTeamAsync(team.Id);
        var emails = await _supervision.GetEmailsAsync(members.Select(m => m.EmployeeId).Distinct());
        return ToDto(team, members, emails);
    }

    private static TeamDto ToDto(
        Team t,
        IReadOnlyCollection<TeamMembership> members,
        IReadOnlyDictionary<string, string> emails) => new()
    {
        Id = t.Id,
        ProjectId = t.ProjectId,
        Name = t.Name,
        LayerCount = t.LayerCount,
        LayerLabels = Deserialize(t.LayerLabels),
        ModuleApprovalConfig = DeserializeConfig(t.ModuleApprovalConfig),
        Members = members
            .OrderByDescending(m => m.Layer)
            .Select(m => new TeamMemberDto
            {
                EmployeeId = m.EmployeeId,
                Email = emails.GetValueOrDefault(m.EmployeeId),
                Layer = m.Layer,
            })
            .ToList(),
    };

    private static string Serialize(List<string> labels) => JsonSerializer.Serialize(labels);

    private static List<string> Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static string SerializeConfig(Dictionary<string, List<int>> config) => JsonSerializer.Serialize(config);

    private static Dictionary<string, List<int>> DeserializeConfig(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, List<int>>>(json) ?? new(); }
        catch { return new(); }
    }
}
