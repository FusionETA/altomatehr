using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Tools;

// Backfill for organisations created before the defaults were seeded at
// creation time. Not part of the running application.
//
// Calls the SAME IOrganizationDefaultsService the create path uses, so a
// backfilled org and a newly created one are identical — a separate SQL script
// would be a second definition of "default" to keep in step with the first.
//
// Scoped to orgs with NO PROJECT OR NO TEAM. An org missing only a policy is
// left alone: it has people working in it who may have deliberately removed
// the seeded policies, and inventing two more would change how everyone there
// is paid. A missing project or team is different — nobody can clock in or get
// approved at all, so there is nothing to disturb.
//
// Runs with no HTTP request, so there is no current org and the tenant filter
// is a no-op: it sees every organisation, which is what a backfill needs.
public static class SeedOrgDefaults
{
    public static async Task RunAsync(IServiceProvider services, bool commit)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var orgs = sp.GetRequiredService<IOrganizationRepository>();
        var projects = sp.GetRequiredService<IProjectRepository>();
        var teams = sp.GetRequiredService<ITeamRepository>();
        var defaults = sp.GetRequiredService<IOrganizationDefaultsService>();

        var allOrgs = await orgs.GetAllAsync();
        var projectOrgs = (await projects.GetAllAsync()).Select(p => p.OrganizationId).ToHashSet();
        var teamOrgs = (await teams.GetAllAsync()).Select(t => t.OrganizationId).ToHashSet();

        var needing = allOrgs
            .Where(o => !projectOrgs.Contains(o.Id) || !teamOrgs.Contains(o.Id))
            .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"organisations                 : {allOrgs.Count}");
        Console.WriteLine($"missing a project or a team   : {needing.Count}");
        foreach (var o in needing)
        {
            var hasProject = projectOrgs.Contains(o.Id);
            var hasTeam = teamOrgs.Contains(o.Id);
            Console.WriteLine(
                $"   {o.Name,-34} project={(hasProject ? "yes" : "NO ")} team={(hasTeam ? "yes" : "NO ")}");
        }

        if (needing.Count == 0) { Console.WriteLine("\nNothing to do."); return; }
        if (!commit)
        {
            Console.WriteLine("\nDRY RUN — nothing written. Add --commit to apply.");
            return;
        }

        foreach (var o in needing)
        {
            var result = await defaults.EnsureForOrganizationAsync(o.Id, o.Name);
            Console.WriteLine(
                $"   {o.Name,-34} policies+{result.PoliciesCreated} "
              + $"project={(result.ProjectCreated ? "created" : "kept")} "
              + $"team={(result.TeamCreated ? "created" : "kept")}");
        }
        Console.WriteLine($"\nCOMMITTED — {needing.Count} organisation(s) seeded");
    }
}
