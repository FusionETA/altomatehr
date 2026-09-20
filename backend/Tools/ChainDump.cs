using System.Text.Json;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Tools;

// Migration verification. Not part of the running application.
//
// v2 does not STORE approval chains — it computes them at request time from the
// team roster, the explicit overrides, each team's module config and the admin
// exclusion. So a freshly loaded TeamMemberships / TeamApprovalOverrides pair
// proves nothing on its own: the only way to know who will actually approve a
// request is to ask the real service. This writes that answer for every
// (employee, project, module) so it can be diffed against v1's stored chains.
//
// Reimplementing those rules in SQL was the alternative, and it is worse: it
// compares v1 against a GUESS at v2, and a bug in the guess is indistinguishable
// from a migration fault. Calling IApprovalChainService is the whole point.
public static class ChainDump
{
    public static async Task RunAsync(IServiceProvider services, string outPath)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var chains = sp.GetRequiredService<IApprovalChainService>();
        var memberships = sp.GetRequiredService<ITeamMembershipRepository>();
        var teams = sp.GetRequiredService<ITeamRepository>();

        // No HTTP request, so there is no current org and the tenant query
        // filter is a no-op — this sees every organization, which is what a
        // migration check needs.
        var teamById = (await teams.GetAllAsync()).ToDictionary(t => t.Id);
        var all = await memberships.GetAllAsync();

        // Keyed per (employee, project): that is the granularity the chain is
        // resolved at, and it lines up with v1's per-team chain because a team
        // belongs to exactly one project.
        var keys = all
            .Where(m => teamById.ContainsKey(m.TeamId))
            .Select(m => (EmployeeId: m.EmployeeId, ProjectId: (string?)teamById[m.TeamId].ProjectId))
            .Distinct()
            .ToList();

        var rows = new List<object>();
        foreach (var module in Enum.GetValues<ApprovalModule>())
        {
            // Bulk: one set of queries per module rather than per employee.
            var byKey = await chains.GetChainsAsync(keys, module);
            foreach (var (key, steps) in byKey)
            {
                rows.Add(new
                {
                    employeeId = key.EmployeeId,
                    projectId = key.ProjectId,
                    module = module.ToString(),
                    steps = steps
                        .Select(s => new { step = s.Step, layer = s.Layer, approverIds = s.ApproverIds })
                        .ToList(),
                });
            }
        }

        await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(rows));
        var withChain = rows.Count(r => ((dynamic)r).steps.Count > 0);
        Console.WriteLine(
            $"chain dump: {keys.Count} (employee, project) pairs x {Enum.GetValues<ApprovalModule>().Length} modules "
          + $"= {rows.Count} rows, {withChain} with a non-empty chain -> {outPath}");
    }
}
