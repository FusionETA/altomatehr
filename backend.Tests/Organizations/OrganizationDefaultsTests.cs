using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Entities;

namespace AltomateHR.Api.Tests.Organizations;

// A new org used to arrive unusable in three ways at once: no policy (no
// module access, no OT rules), no project (nowhere to clock in), no team (no
// approval chain). These pin that it arrives usable, and that running the
// backfill over an org that already has some of it changes only what is
// missing.
public class OrganizationDefaultsTests
{
    private const string Org = "org-1";

    [Fact]
    public async Task ANewOrganisationGetsAPolicyAProjectAndATeam()
    {
        var (service, policies, projects, teams) = Build();

        var result = await service.EnsureForOrganizationAsync(Org, "Acme Sdn Bhd");

        Assert.Equal(2, result.PoliciesCreated);
        Assert.True(result.ProjectCreated);
        Assert.True(result.TeamCreated);

        Assert.Equal("Acme Sdn Bhd Project (default)", Assert.Single(projects.Rows).Name);

        var team = Assert.Single(teams.Rows);
        Assert.Equal("Acme Sdn Bhd Team (default)", team.Name);
        Assert.Equal(projects.Rows[0].Id, team.ProjectId);
        // One layer: everyone at layer 0 with nobody above, so requests
        // auto-approve until an admin builds a hierarchy.
        Assert.Equal(1, team.LayerCount);
    }

    // The FIRST policy an org gets becomes its default, and a monthly salary is
    // the common case. Created the other way round, hourly workers would
    // inherit a default that prorates them wrongly.
    [Fact]
    public async Task TheMonthlyPolicyIsTheDefault_NotTheHourlyOne()
    {
        var (service, policies, _, _) = Build();

        await service.EnsureForOrganizationAsync(Org, "Acme");

        var monthly = policies.Rows.Single(p => p.SalaryType == SalaryType.MONTHLY);
        var hourly = policies.Rows.Single(p => p.SalaryType == SalaryType.HOURLY);

        Assert.True(monthly.IsDefault);
        Assert.False(hourly.IsDefault);
        Assert.Equal("Monthly Workers", monthly.Name);
    }

    [Fact]
    public async Task EveryRowCarriesTheTargetOrg_NotTheAmbientOne()
    {
        // The tenant stamp only fills a BLANK OrganizationId, and both callers
        // run against an org that is not the ambient one — creation has the
        // caller's OLD org, the backfill has none at all.
        var (service, policies, projects, teams) = Build();

        await service.EnsureForOrganizationAsync(Org, "Acme");

        Assert.All(policies.Rows, p => Assert.Equal(Org, p.OrganizationId));
        Assert.All(projects.Rows, p => Assert.Equal(Org, p.OrganizationId));
        Assert.All(teams.Rows, t => Assert.Equal(Org, t.OrganizationId));
    }

    [Fact]
    public async Task RunningItTwiceChangesNothingTheSecondTime()
    {
        var (service, policies, projects, teams) = Build();

        await service.EnsureForOrganizationAsync(Org, "Acme");
        var second = await service.EnsureForOrganizationAsync(Org, "Acme");

        Assert.False(second.AnythingCreated);
        Assert.Equal(2, policies.Rows.Count);
        Assert.Single(projects.Rows);
        Assert.Single(teams.Rows);
    }

    // Each aggregate is checked on its own, so an org that kept its project but
    // lost its team gets only the team — and its existing policies are left
    // alone rather than joined by two more that would change how people are paid.
    [Fact]
    public async Task AnOrgWithAProjectAndPoliciesGetsOnlyTheMissingTeam()
    {
        var (service, policies, projects, teams) = Build();
        policies.Rows.Add(new EmployeePolicy { OrganizationId = Org, Name = "Site Crew", IsDefault = true });
        projects.Rows.Add(new Project { Id = "p-existing", OrganizationId = Org, Name = "Head Office" });

        var result = await service.EnsureForOrganizationAsync(Org, "Acme");

        Assert.Equal(0, result.PoliciesCreated);
        Assert.False(result.ProjectCreated);
        Assert.True(result.TeamCreated);

        Assert.Equal("Site Crew", Assert.Single(policies.Rows).Name);
        // Hung off the project it already had, not a second one.
        Assert.Equal("p-existing", Assert.Single(teams.Rows).ProjectId);
    }

    [Fact]
    public async Task AnotherOrgsRowsAreNotMistakenForThisOnes()
    {
        var (service, _, projects, teams) = Build();
        projects.Rows.Add(new Project { Id = "p-other", OrganizationId = "org-2", Name = "Someone else" });
        teams.Rows.Add(new Team { Id = "t-other", OrganizationId = "org-2", ProjectId = "p-other" });

        var result = await service.EnsureForOrganizationAsync(Org, "Acme");

        Assert.True(result.ProjectCreated);
        Assert.True(result.TeamCreated);
    }

    private static (IOrganizationDefaultsService, FakePolicies, FakeProjects, FakeTeams) Build()
    {
        var policies = new FakePolicies();
        var projects = new FakeProjects();
        var teams = new FakeTeams();
        return (new OrganizationDefaultsService(policies, projects, teams), policies, projects, teams);
    }

    // The repositories are unfiltered here, which matches the backfill's
    // context — no request, so no current org and no query filter. The service
    // must narrow by OrganizationId itself, and these fakes prove it does.
    private sealed class FakePolicies : IEmployeePolicyRepository
    {
        public List<EmployeePolicy> Rows { get; } = [];
        public Task<List<EmployeePolicy>> GetAllAsync() => Task.FromResult(Rows);
        public Task<List<EmployeePolicy>> GetAllAcrossOrgsAsync() => Task.FromResult(Rows);
        public Task<EmployeePolicy?> GetByIdAsync(string id) =>
            Task.FromResult(Rows.FirstOrDefault(p => p.Id == id));
        public Task<EmployeePolicy?> GetByNameAsync(string name) =>
            Task.FromResult(Rows.FirstOrDefault(p => p.Name == name));
        public Task<EmployeePolicy?> GetDefaultAsync() =>
            Task.FromResult(Rows.FirstOrDefault(p => p.IsDefault));
        public Task<EmployeePolicy> AddAsync(EmployeePolicy policy)
        {
            Rows.Add(policy);
            return Task.FromResult(policy);
        }
        public Task UpdateAsync(EmployeePolicy policy) => Task.CompletedTask;
        public Task ClearDefaultExceptAsync(string policyId) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    private sealed class FakeProjects : IProjectRepository
    {
        public List<Project> Rows { get; } = [];
        public Task<List<Project>> GetAllAsync() => Task.FromResult(Rows);
        public Task<Project?> GetByIdAsync(string id) =>
            Task.FromResult(Rows.FirstOrDefault(p => p.Id == id));
        public Task<Project> AddAsync(Project project)
        {
            Rows.Add(project);
            return Task.FromResult(project);
        }
        public Task UpdateAsync(Project project) => Task.CompletedTask;
        public Task<List<ProjectGeofencePoint>> GetGeofencePointsAsync(string projectId) =>
            Task.FromResult(new List<ProjectGeofencePoint>());
        public Task<List<ProjectAllowedIp>> GetAllowedIpsAsync(string projectId) =>
            Task.FromResult(new List<ProjectAllowedIp>());
        public Task ReplaceGeofencePointsAsync(
            string projectId, IReadOnlyList<ProjectGeofencePoint> points) => Task.CompletedTask;
        public Task ReplaceAllowedIpsAsync(
            string projectId, IReadOnlyList<ProjectAllowedIp> entries) => Task.CompletedTask;
        public Task<Dictionary<string, int>> GetGeofencePointCountsAsync() =>
            Task.FromResult(new Dictionary<string, int>());
        public Task<Dictionary<string, int>> GetAllowedIpCountsAsync() =>
            Task.FromResult(new Dictionary<string, int>());
    }

    private sealed class FakeTeams : ITeamRepository
    {
        public List<Team> Rows { get; } = [];
        public Task<List<Team>> GetAllAsync() => Task.FromResult(Rows);
        public Task<Team?> GetByIdAsync(string id) =>
            Task.FromResult(Rows.FirstOrDefault(t => t.Id == id));
        public Task<Team?> GetByProjectAndNameAsync(string projectId, string name) =>
            Task.FromResult(Rows.FirstOrDefault(t => t.ProjectId == projectId && t.Name == name));
        public Task<Team> AddAsync(Team team)
        {
            Rows.Add(team);
            return Task.FromResult(team);
        }
        public Task UpdateAsync(Team team) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }
}
