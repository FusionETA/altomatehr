using System.Text.Json;
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
    // Empty when: the employee is on no team, sits at/above the top approving
    // layer, or the module is configured to skip approvals.
    Task<IReadOnlyList<ApprovalStep>> GetChainAsync(string employeeId, ApprovalModule module);
}

public class ApprovalChainService : IApprovalChainService
{
    private readonly ITeamRepository _teams;
    private readonly ITeamMembershipRepository _memberships;

    public ApprovalChainService(ITeamRepository teams, ITeamMembershipRepository memberships)
    {
        _teams = teams;
        _memberships = memberships;
    }

    public async Task<IReadOnlyList<ApprovalStep>> GetChainAsync(string employeeId, ApprovalModule module)
    {
        // Primary team = the first by id. Multi-team routing is a later refinement.
        var mine = (await _memberships.GetByEmployeeAsync(employeeId))
            .OrderBy(m => m.TeamId)
            .ToList();
        if (mine.Count == 0) return [];

        var membership = mine[0];
        var team = await _teams.GetByIdAsync(membership.TeamId);
        if (team is null) return [];

        var roster = await _memberships.GetByTeamAsync(team.Id);
        var labels = DeserializeList(team.LayerLabels);
        // Null → all layers approve (module unconfigured); a set (possibly empty)
        // → only those layers approve.
        var allowedLayers = ModuleLayers(team.ModuleApprovalConfig, module);

        var steps = new List<ApprovalStep>();
        for (var layer = membership.Layer + 1; layer < team.LayerCount; layer++)
        {
            if (allowedLayers is not null && !allowedLayers.Contains(layer)) continue;   // not a required approver layer

            var approvers = roster
                .Where(m => m.Layer == layer && m.EmployeeId != employeeId)
                .Select(m => m.EmployeeId)
                .Distinct()
                .ToList();
            if (approvers.Count == 0) continue;   // skip empty layers — don't block on a vacancy

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
