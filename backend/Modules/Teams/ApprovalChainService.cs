using System.Text.Json;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Teams.Entities;

namespace AltomateHR.Api.Modules.Teams;

// Modules whose approvals can route through a team chain. Only CLAIMS and LEAVE
// are wired today; OT/ATTENDANCE exist for the per-module config's forward-compat.
public enum ApprovalModule
{
    CLAIMS,
    LEAVE,
    OT,
    ATTENDANCE,
}

// One step in an employee's approval chain: the approvers are the members at
// `Layer`. Any one of them approving advances to the next step.
public record ApprovalStep(int Step, int Layer, string LayerLabel, IReadOnlyList<string> ApproverIds);

public interface IApprovalChainService
{
    // The ordered approval chain for an employee for a given module, derived
    // from their team's layers filtered by the team's module-approval config.
    // `projectId` disambiguates which team governs when the employee is on
    // more than one — see the resolution rule inside GetChainAsync. Empty
    // when: the employee is on no team, sits at/above the top approving
    // layer, or the module is configured to skip approvals.
    Task<IReadOnlyList<ApprovalStep>> GetChainAsync(
        string employeeId, ApprovalModule module, string? projectId = null);

    // The same answer for many (employee, project) pairs, in a fixed number of
    // queries rather than one set per pair.
    //
    // Resolving a chain touches five tables, so a caller asking "which of these
    // 40 pending requests are mine to approve?" one at a time paid 40 round
    // trips to a remote database — the single biggest cost in that endpoint.
    // Every read here is org-scoped by the tenant filter and the tables are
    // small, so loading them whole once beats fetching slices repeatedly.
    Task<IReadOnlyDictionary<(string EmployeeId, string? ProjectId), IReadOnlyList<ApprovalStep>>> GetChainsAsync(
        IReadOnlyCollection<(string EmployeeId, string? ProjectId)> keys, ApprovalModule module);
}

public class ApprovalChainService : IApprovalChainService
{
    private readonly ITeamRepository _teams;
    private readonly ITeamMembershipRepository _memberships;
    private readonly ISupervisionService _supervision;
    private readonly ITeamApprovalOverrideRepository _overrides;
    private readonly IProjectRepository _projects;

    public ApprovalChainService(
        ITeamRepository teams,
        ITeamMembershipRepository memberships,
        ISupervisionService supervision,
        ITeamApprovalOverrideRepository overrides,
        IProjectRepository projects)
    {
        _teams = teams;
        _memberships = memberships;
        _supervision = supervision;
        _overrides = overrides;
        _projects = projects;
    }

    // Everything a chain build reads, loaded once. Held as a local for the
    // duration of one call and then dropped: nothing is cached between calls,
    // because a request that edits a team and then re-resolves chains (see
    // TeamsController's reconcile-after-mutation) must see the edit.
    private sealed record ChainData(
        ILookup<string, TeamMembership> MembershipsByEmployee,
        IReadOnlyDictionary<string, Team> TeamsById,
        ILookup<string, TeamMembership> RosterByTeam,
        IReadOnlyCollection<string> Administrative,
        IReadOnlyDictionary<string, string> ProjectNames,
        ILookup<(string TeamId, string EmployeeId), TeamApprovalOverride> OverridesByMember);

    private async Task<ChainData> LoadAsync()
    {
        var memberships = await _memberships.GetAllAsync();
        var teams = await _teams.GetAllAsync();
        var administrative = await _supervision.GetAdministrativeUserIdsAsync();
        var projects = await _projects.GetAllAsync();
        var overrides = await _overrides.GetAllAsync();

        return new ChainData(
            memberships.ToLookup(m => m.EmployeeId),
            teams.ToDictionary(t => t.Id),
            memberships.ToLookup(m => m.TeamId),
            administrative,
            projects.ToDictionary(p => p.Id, p => p.Name),
            overrides.ToLookup(o => (o.TeamId, o.EmployeeId)));
    }

    public async Task<IReadOnlyList<ApprovalStep>> GetChainAsync(
        string employeeId, ApprovalModule module, string? projectId = null) =>
        Build(employeeId, projectId, module, await LoadAsync());

    public async Task<IReadOnlyDictionary<(string EmployeeId, string? ProjectId), IReadOnlyList<ApprovalStep>>> GetChainsAsync(
        IReadOnlyCollection<(string EmployeeId, string? ProjectId)> keys, ApprovalModule module)
    {
        var distinct = keys.Distinct().ToList();
        if (distinct.Count == 0)
            return new Dictionary<(string, string?), IReadOnlyList<ApprovalStep>>();

        var data = await LoadAsync();
        return distinct.ToDictionary(k => k, k => Build(k.EmployeeId, k.ProjectId, module, data));
    }

    // The rule itself, in memory. Identical for one employee or forty — there is
    // no second copy of this logic to drift from the first.
    private static IReadOnlyList<ApprovalStep> Build(
        string employeeId, string? projectId, ApprovalModule module, ChainData data)
    {
        var mine = data.MembershipsByEmployee[employeeId].ToList();
        if (mine.Count == 0) return [];

        // Each membership's team, kept alongside it so both the project-match
        // and the alphabetical fallback below can be resolved in one pass.
        var withTeams = new List<(TeamMembership Membership, Team Team)>();
        foreach (var m in mine)
        {
            if (data.TeamsById.TryGetValue(m.TeamId, out var t)) withTeams.Add((m, t));
        }
        if (withTeams.Count == 0) return [];

        // An employee can be on several teams across several projects (e.g.
        // Claims/Attendance/OT are project-scoped). Resolution, matching the
        // reference app's resolveModuleChain:
        //   1. If the request names a project, use the team that matches it.
        //   2. Otherwise — or if nothing matched — fall back to whichever
        //      team's PROJECT NAME sorts first alphabetically (Leave has no
        //      project concept at all, so it always takes this branch).
        // Either way, ties are broken by team id for determinism.
        (TeamMembership Membership, Team Team)? resolved = null;
        if (projectId is not null)
        {
            var matches = withTeams
                .Where(x => x.Team.ProjectId == projectId)
                .OrderBy(x => x.Team.Id, StringComparer.Ordinal)
                .ToList();
            if (matches.Count > 0) resolved = matches[0];
        }
        if (resolved is null)
        {
            resolved = withTeams
                .OrderBy(x => data.ProjectNames.GetValueOrDefault(x.Team.ProjectId, ""), StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Team.Id, StringComparer.Ordinal)
                .First();
        }

        var (membership, team) = resolved.Value;

        var roster = data.RosterByTeam[team.Id].ToList();
        // Admins/owners are oversight, not links in the chain — an admin sitting
        // in a team must not become anyone's approver. Subtracted here rather
        // than at the decision point so they never even APPEAR as an approver,
        // including in the chain the UI shows. See OrgRoles.
        var administrative = data.Administrative;
        var labels = DeserializeList(team.LayerLabels);
        // Null → all layers approve (module unconfigured); a set (possibly empty)
        // → only those layers approve.
        var allowedLayers = ModuleLayers(team.ModuleApprovalConfig, module);

        // Explicit, admin-picked overrides for THIS employee, keyed by layer.
        // Absent for a layer → fall back to the implicit default below.
        var overridesByLayer = data.OverridesByMember[(team.Id, employeeId)]
            .ToDictionary(o => o.Layer);

        var steps = new List<ApprovalStep>();
        for (var layer = membership.Layer + 1; layer < team.LayerCount; layer++)
        {
            if (allowedLayers is not null && !allowedLayers.Contains(layer)) continue;   // not a required approver layer

            List<string> approvers;
            if (overridesByLayer.TryGetValue(layer, out var ov))
            {
                // Explicit. Re-filtered against who's actually still at this
                // layer and not administrative — defense-in-depth in case the
                // roster changed since the override was saved. An override
                // with an empty list means this employee deliberately has no
                // approver at this layer, distinct from no override at all.
                var validAtLayer = roster.Where(m => m.Layer == layer).Select(m => m.EmployeeId).ToHashSet();
                approvers = DeserializeList(ov.ApproverIdsJson)
                    .Where(id => validAtLayer.Contains(id) && !administrative.Contains(id))
                    .Distinct()
                    .ToList();
            }
            else
            {
                // Implicit default: everyone else at this layer.
                approvers = roster
                    .Where(m => m.Layer == layer
                                && m.EmployeeId != employeeId
                                && !administrative.Contains(m.EmployeeId))
                    .Select(m => m.EmployeeId)
                    .Distinct()
                    .ToList();
            }
            if (approvers.Count == 0) continue;   // vacant by default, or explicitly emptied — skip either way

            steps.Add(new ApprovalStep(steps.Count, layer, LabelFor(labels, layer), approvers));
        }
        return steps;
    }

    private static HashSet<int>? ModuleLayers(string configJson, ApprovalModule module)
    {
        if (string.IsNullOrWhiteSpace(configJson) || configJson == "{}") return null;
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, List<int>>>(configJson);
            if (map is null || !map.TryGetValue(module.ToString(), out var layers)) return null;   // absent → all layers
            return layers.ToHashSet();
        }
        catch
        {
            return null;
        }
    }

    private static string LabelFor(IReadOnlyList<string> labels, int layer) =>
        layer < labels.Count && !string.IsNullOrWhiteSpace(labels[layer]) ? labels[layer] : $"Layer {layer + 1}";

    private static List<string> DeserializeList(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }
}
