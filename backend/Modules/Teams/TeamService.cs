using AltomateHR.Api.Modules.Employees;
using System.Text.Json;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Teams.Dtos;
using AltomateHR.Api.Modules.Teams.Entities;

using AltomateHR.Api.Modules.Audit;

namespace AltomateHR.Api.Modules.Teams;

// Owns team CRUD + membership. Employee emails are resolved through the Auth
// supervision service so a team roster shows who's on it.
public class TeamService : ITeamService
{
    private readonly ITeamRepository _teams;
    private readonly ITeamMembershipRepository _memberships;
    private readonly ISupervisionService _supervision;
    private readonly IApprovalChainService _chain;
    private readonly ITeamApprovalOverrideRepository _overrides;
    private readonly IAuditService _audit;

    public TeamService(
        ITeamRepository teams,
        ITeamMembershipRepository memberships,
        ISupervisionService supervision,
        IApprovalChainService chain,
        ITeamApprovalOverrideRepository overrides,
        IAuditService audit)
    {
        _teams = teams;
        _memberships = memberships;
        _supervision = supervision;
        _chain = chain;
        _overrides = overrides;
        _audit = audit;
    }

    public async Task<IEnumerable<ApprovalStepDto>> GetApprovalChainAsync(
        string employeeId, ApprovalModule module, string? projectId = null)
    {
        var chain = await _chain.GetChainAsync(employeeId, module, projectId);
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

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.TeamCreate,
            $"{team.Name} · {team.LayerCount} layers",
            TargetType: "Team",
            TargetId: team.Id,
            Metadata: new { team.Name, team.ProjectId, team.LayerCount }));

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

        // Layer count and the per-module config together decide the approval
        // chain, so both sides are recorded: shrinking the layers is what leaves
        // a pending request with nobody above it.
        var previousLayerCount = team.LayerCount;
        var previousConfig = team.ModuleApprovalConfig;

        team.Name = name;
        team.LayerCount = dto.LayerCount;
        team.LayerLabels = Serialize(dto.LayerLabels);
        team.ModuleApprovalConfig = SerializeConfig(dto.ModuleApprovalConfig);
        team.UpdatedAt = DateTime.UtcNow;
        await _teams.UpdateAsync(team);
        // A shrunk LayerCount can leave overrides pointing at layers that no
        // longer exist — prune them, same reasoning as healing stranded
        // approval requests on this same edit.
        await _overrides.DeleteLayersAboveAsync(id, dto.LayerCount);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.TeamUpdate,
            team.Name,
            TargetType: "Team",
            TargetId: team.Id,
            Metadata: new
            {
                team.Name,
                Layers = new { From = previousLayerCount, To = team.LayerCount },
                ModuleConfig = new { From = previousConfig, To = team.ModuleApprovalConfig },
            }));

        return new TeamSaveResult(true, await BuildAsync(team), null);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var team = await _teams.GetByIdAsync(id);
        if (team is null) return false;

        await _memberships.DeleteByTeamAsync(id);   // clear the roster first
        await _overrides.DeleteByTeamAsync(id);
        await _teams.DeleteAsync(id);

        // Deleting a team removes every approval chain derived from it, which is
        // exactly how requests end up with no approver to route to.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.TeamDelete,
            $"{team.Name} and its whole roster",
            TargetType: "Team",
            TargetId: team.Id,
            Metadata: new { team.Name, team.ProjectId, team.LayerCount }));

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
        int? previousLayer = null;
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
            previousLayer = existing.Layer;
            existing.Layer = dto.Layer;
            existing.UpdatedAt = now;
            await _memberships.UpdateAsync(existing);
        }

        // A layer move is the finest-grained change that alters an approval
        // chain — this is the event that explains why a request suddenly routes
        // somewhere else, or nowhere.
        var who = known[dto.EmployeeId];
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.TeamMemberSet,
            // Terse on purpose: the Action column already says "Team member
            // added or moved", and the before → after lives in the expandable
            // detail. Repeating all three made the row unreadable at a glance.
            $"{who} → {team.Name} layer {dto.Layer}",
            TargetType: "Team",
            TargetId: team.Id,
            Metadata: new
            {
                team.Name,
                Employee = who,
                dto.EmployeeId,
                Layer = new { From = previousLayer, To = dto.Layer },
            }));

        return new TeamSaveResult(true, await BuildAsync(team), null);
    }

    public async Task<TeamSaveResult> RemoveMemberAsync(string teamId, string employeeId)
    {
        var team = await _teams.GetByIdAsync(teamId);
        if (team is null) return new TeamSaveResult(false, null, null);

        var membership = await _memberships.GetByTeamAndEmployeeAsync(teamId, employeeId);
        if (membership is not null)
        {
            await _memberships.DeleteAsync(membership.Id);
            // Their whole override config is moot once they leave the team — a
            // re-add later should start from a fresh default, not stale picks.
            await _overrides.DeleteByTeamAndEmployeeAsync(teamId, employeeId);

            // Taking the last person out of a layer empties it. The chain skips
            // empty layers, so this can silently shorten everyone's approval
            // path — worth a row saying who did it.
            var emails = await _supervision.GetEmailsAsync([employeeId]);
            await _audit.WriteAsync(new AuditEvent(
                AuditActions.TeamMemberRemove,
                $"{emails.GetValueOrDefault(employeeId, employeeId)} out of {team.Name}",
                TargetType: "Team",
                TargetId: team.Id,
                Metadata: new { team.Name, EmployeeId = employeeId, membership.Layer }));
        }

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
    public async Task<IReadOnlyList<SupervisedTeamDto>> GetSupervisedTeamsAsync(string userId)
    {
        var mine = await _memberships.GetByEmployeeAsync(userId);
        if (mine.Count == 0) return [];

        var supervised = new List<SupervisedTeamDto>();
        foreach (var membership in mine)
        {
            var team = await _teams.GetByIdAsync(membership.TeamId);
            if (team is null) continue;

            var below = (await _memberships.GetByTeamAsync(team.Id))
                .Where(m => m.Layer < membership.Layer)
                .Select(m => m.EmployeeId)
                .Distinct()
                .ToList();

            // A team where nobody sits below the caller isn't one they oversee.
            // Skipping it keeps empty project tabs from appearing.
            if (below.Count == 0) continue;

            supervised.Add(new SupervisedTeamDto
            {
                TeamId = team.Id,
                TeamName = team.Name,
                ProjectId = team.ProjectId,
                MemberIds = below,
            });
        }

        return supervised;
    }

    public async Task<IReadOnlyList<string>> GetReportEmployeeIdsAsync(string supervisorId) =>
        (await GetSupervisedTeamsAsync(supervisorId))
            .SelectMany(t => t.MemberIds)
            .Distinct()
            .ToList();

    public async Task<IReadOnlyList<string>> GetProjectIdsForMemberAsync(string employeeId)
    {
        var mine = await _memberships.GetByEmployeeAsync(employeeId);
        if (mine.Count == 0) return [];

        var projectIds = new List<string>();
        foreach (var membership in mine)
        {
            var team = await _teams.GetByIdAsync(membership.TeamId);
            if (team is not null && !projectIds.Contains(team.ProjectId)) projectIds.Add(team.ProjectId);
        }
        return projectIds;
    }



    public async Task<IReadOnlyList<string>> GetMemberEmployeeIdsAsync(string teamId) =>
        (await _memberships.GetByTeamAsync(teamId)).Select(m => m.EmployeeId).ToList();

    public async Task<IReadOnlyList<LayerApproverOptionsDto>?> GetApproverOptionsAsync(string teamId, string employeeId)
    {
        var team = await _teams.GetByIdAsync(teamId);
        if (team is null) return null;

        var membership = await _memberships.GetByTeamAndEmployeeAsync(teamId, employeeId);
        if (membership is null) return null;   // not a member of this team

        var roster = await _memberships.GetByTeamAsync(teamId);
        var administrative = await _supervision.GetAdministrativeUserIdsAsync();
        var overridesByLayer = (await _overrides.GetByTeamAndEmployeeAsync(teamId, employeeId))
            .ToDictionary(o => o.Layer);
        var labels = Deserialize(team.LayerLabels);
        var emails = await _supervision.GetEmailsAsync(roster.Select(m => m.EmployeeId).Distinct());

        var options = new List<LayerApproverOptionsDto>();
        for (var layer = membership.Layer + 1; layer < team.LayerCount; layer++)
        {
            // The implicit default: everyone else at this layer, minus admins.
            var candidateIds = roster
                .Where(m => m.Layer == layer && m.EmployeeId != employeeId && !administrative.Contains(m.EmployeeId))
                .Select(m => m.EmployeeId)
                .Distinct()
                .ToList();

            var isOverridden = overridesByLayer.TryGetValue(layer, out var ov);
            var effective = isOverridden
                ? Deserialize(ov!.ApproverIdsJson).Where(id => candidateIds.Contains(id)).ToList()
                : candidateIds;

            options.Add(new LayerApproverOptionsDto
            {
                Layer = layer,
                LayerLabel = LabelFor(labels, layer),
                Candidates = candidateIds
                    .Select(id => new ApproverCandidateDto { EmployeeId = id, Email = emails.GetValueOrDefault(id) })
                    .ToList(),
                EffectiveApproverIds = effective,
                IsOverridden = isOverridden,
            });
        }
        return options;
    }

    public async Task<ApproverOverrideResult> SetApproverOverrideAsync(
        string teamId, string employeeId, int layer, List<string> approverIds)
    {
        var team = await _teams.GetByIdAsync(teamId);
        if (team is null) return new ApproverOverrideResult(false, null, null);

        var membership = await _memberships.GetByTeamAndEmployeeAsync(teamId, employeeId);
        if (membership is null) return new ApproverOverrideResult(false, null, null);

        if (layer <= membership.Layer || layer >= team.LayerCount)
            return new ApproverOverrideResult(false, null, "That layer isn't above this employee's own layer.");

        var roster = await _memberships.GetByTeamAsync(teamId);
        var administrative = await _supervision.GetAdministrativeUserIdsAsync();
        var validAtLayer = roster.Where(m => m.Layer == layer).Select(m => m.EmployeeId).ToHashSet();
        var distinctIds = approverIds.Distinct().ToList();
        if (distinctIds.Any(id => !validAtLayer.Contains(id) || administrative.Contains(id)))
            return new ApproverOverrideResult(
                false, null, "Every approver must be a member of this team at that exact layer.");

        var now = DateTime.UtcNow;
        await _overrides.UpsertAsync(new TeamApprovalOverride
        {
            TeamId = teamId,
            EmployeeId = employeeId,
            Layer = layer,
            ApproverIdsJson = Serialize(distinctIds),
            CreatedAt = now,
            UpdatedAt = now,
        });

        return new ApproverOverrideResult(true, await GetApproverOptionsAsync(teamId, employeeId), null);
    }

    public async Task<ApproverOverrideResult> ClearApproverOverrideAsync(string teamId, string employeeId, int layer)
    {
        var team = await _teams.GetByIdAsync(teamId);
        if (team is null) return new ApproverOverrideResult(false, null, null);

        var membership = await _memberships.GetByTeamAndEmployeeAsync(teamId, employeeId);
        if (membership is null) return new ApproverOverrideResult(false, null, null);

        await _overrides.DeleteAsync(teamId, employeeId, layer);
        return new ApproverOverrideResult(true, await GetApproverOptionsAsync(teamId, employeeId), null);
    }

    private static string LabelFor(IReadOnlyList<string> labels, int layer) =>
        layer < labels.Count && !string.IsNullOrWhiteSpace(labels[layer]) ? labels[layer] : $"Layer {layer + 1}";
}
