using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Dtos;
using AltomateHR.Api.Modules.Xero;

using AltomateHR.Api.Tests.Audit;

namespace AltomateHR.Api.Tests.Projects;

// What the clock-in and claim pickers offer. An archived project is refused by
// the server exactly like another team's is, so listing it only invites the
// rejection — and connecting Xero archives every hand-created project, which is
// how archived ones started appearing in the employee's picker at all.
public class ProjectsForMemberTests
{
    [Fact]
    public async Task GetForMemberAsync_LeavesOutArchivedProjects()
    {
        var service = Create(
            [Project("prj-live", "Office"), Archived("prj-old", "Mobile App")],
            onProjects: ["prj-live", "prj-old"]);

        var mine = await service.GetForMemberAsync("usr-emp");

        Assert.Equal(["Office"], mine.Select(p => p.Name));
    }

    [Fact]
    public async Task GetForMemberAsync_LeavesOutProjectsTheUserIsNotOn()
    {
        var service = Create(
            [Project("prj-mine", "Office"), Project("prj-theirs", "ZR")],
            onProjects: ["prj-mine"]);

        var mine = await service.GetForMemberAsync("usr-emp");

        Assert.Equal(["Office"], mine.Select(p => p.Name));
    }

    [Fact]
    public async Task GetAllAsync_StillReturnsArchivedProjects()
    {
        // The admin list has to show them — that's where they get restored.
        var service = Create([Project("prj-live", "Office"), Archived("prj-old", "Mobile App")]);

        var all = await service.GetAllAsync();

        Assert.Equal(2, all.Count());
    }

    // ---- switching the Xero tracking category ----
    //
    // A Xero org can hold projects in either of two tracking categories. After
    // the admin switches, the old category's projects must stop being offered
    // — a picker mixing "Region" options with projects is wrong — but must not
    // be deleted, because past claims and shifts still point at them.

    [Fact]
    public async Task GetForMemberAsync_LeavesOutProjectsFromTheCategorySwitchedAwayFrom()
    {
        var service = Create(
            [FromCategory("prj-new", "Tower A", "cat-projects"), FromCategory("prj-old", "Penang", "cat-regions")],
            onProjects: ["prj-new", "prj-old"],
            activeCategory: "cat-projects");

        var mine = await service.GetForMemberAsync("usr-emp");

        Assert.Equal(["Tower A"], mine.Select(p => p.Name));
    }

    // Hand-made and Xero Projects-API projects have no tracking option, so no
    // category to be switched away from. They stay.
    [Fact]
    public async Task GetForMemberAsync_KeepsProjectsThatDidNotComeFromACategory()
    {
        var service = Create(
            [Project("prj-manual", "Office"), FromCategory("prj-old", "Penang", "cat-regions")],
            onProjects: ["prj-manual", "prj-old"],
            activeCategory: "cat-projects");

        var mine = await service.GetForMemberAsync("usr-emp");

        Assert.Equal(["Office"], mine.Select(p => p.Name));
    }

    // Synced before the category was recorded: whether it is current can't be
    // told, and hiding a current project would be worse than showing an old one.
    [Fact]
    public async Task GetForMemberAsync_KeepsATrackedProjectWhoseCategoryIsUnknown()
    {
        var legacy = new Project { Id = "prj-legacy", Name = "Legacy", XeroTrackingOptionId = "opt-1" };
        var service = Create([legacy], onProjects: ["prj-legacy"], activeCategory: "cat-projects");

        Assert.Single(await service.GetForMemberAsync("usr-emp"));
    }

    // No category chosen (or Xero not connected): nothing is hidden.
    [Fact]
    public async Task GetForMemberAsync_HidesNothingWhenNoCategoryIsChosen()
    {
        var service = Create(
            [FromCategory("prj-a", "Tower A", "cat-projects"), FromCategory("prj-b", "Penang", "cat-regions")],
            onProjects: ["prj-a", "prj-b"],
            activeCategory: null);

        Assert.Equal(2, (await service.GetForMemberAsync("usr-emp")).Count());
    }

    // The full list keeps every project — other modules look names up in it
    // for past records — and flags the switched-out ones for pickers.
    [Fact]
    public async Task GetAllAsync_KeepsSwitchedOutProjectsButFlagsThem()
    {
        var service = Create(
            [FromCategory("prj-new", "Tower A", "cat-projects"), FromCategory("prj-old", "Penang", "cat-regions")],
            activeCategory: "cat-projects");

        var all = (await service.GetAllAsync()).ToDictionary(p => p.Id);

        Assert.Equal(2, all.Count);
        Assert.False(all["prj-new"].HiddenByTrackingCategory);
        Assert.True(all["prj-old"].HiddenByTrackingCategory);
    }

    // The project a SAVE returns replaces the card in the list. Its site count
    // came back 0, and since saving also clears the legacy lat/long pair, the
    // card said "No geofence" straight after a site was added.
    [Fact]
    public async Task UpdateAsync_ReturnsTheSiteAndIpCountsTheListShows()
    {
        var service = Create([Project("prj-a", "Project Alpha")]);

        var saved = await service.UpdateAsync("prj-a", new Modules.Projects.Dtos.SaveProjectDto
        {
            Name = "Project Alpha",
            GeofencePoints = [new() { Label = "Gate", Latitude = 3.06, Longitude = 101.5 }],
            AllowedIpEntries = [new() { Label = "HQ", Cidr = "203.106.51.0/24" }],
        });

        Assert.NotNull(saved);
        Assert.Equal(1, saved!.GeofenceSiteCount);
        Assert.Equal(1, saved.AllowedIpCount);
        Assert.Single(saved.GeofencePoints);
    }

    // ---- wiring ----

    private static Project Project(string id, string name) => new() { Id = id, Name = name };

    private static Project FromCategory(string id, string name, string categoryId) =>
        new() { Id = id, Name = name, XeroTrackingOptionId = $"opt-{id}", XeroTrackingCategoryId = categoryId };

    private static Project Archived(string id, string name) =>
        new() { Id = id, Name = name, IsArchived = true };

    private static ProjectService Create(
        List<Project> projects, string[]? onProjects = null, string? activeCategory = null) =>
        new(new FakeProjectRepository(projects),
            new FakeAuditService(),
            new FakeProjectTeamService(onProjects ?? []),
            new FixedTrackingScope(activeCategory));
}

internal sealed class FixedTrackingScope(string? activeCategoryId) : IProjectTrackingScope
{
    public Task<string?> GetActiveCategoryIdAsync() => Task.FromResult(activeCategoryId);
}

internal sealed class FakeProjectRepository : IProjectRepository
{
        // Keeps what a save replaced, so a test can read it back.
        private readonly Dictionary<string, List<ProjectGeofencePoint>> _sites = new();
        private readonly Dictionary<string, List<ProjectAllowedIp>> _ips = new();

        public Task<List<ProjectGeofencePoint>> GetGeofencePointsAsync(string projectId) =>
            Task.FromResult(_sites.GetValueOrDefault(projectId) ?? new List<ProjectGeofencePoint>());
        public Task<List<ProjectAllowedIp>> GetAllowedIpsAsync(string projectId) =>
            Task.FromResult(_ips.GetValueOrDefault(projectId) ?? new List<ProjectAllowedIp>());
        public Task ReplaceGeofencePointsAsync(string projectId, IReadOnlyList<ProjectGeofencePoint> points)
        {
            _sites[projectId] = points.ToList();
            return Task.CompletedTask;
        }
        public Task ReplaceAllowedIpsAsync(string projectId, IReadOnlyList<ProjectAllowedIp> entries)
        {
            _ips[projectId] = entries.ToList();
            return Task.CompletedTask;
        }
        public Task<Dictionary<string, int>> GetGeofencePointCountsAsync() =>
            Task.FromResult(new Dictionary<string, int>());
        public Task<Dictionary<string, int>> GetAllowedIpCountsAsync() =>
            Task.FromResult(new Dictionary<string, int>());



    private readonly List<Project> _projects;

    public FakeProjectRepository(List<Project> projects) => _projects = projects;

    public Task<List<Project>> GetAllAsync() => Task.FromResult(_projects);
    public Task<Project?> GetByIdAsync(string id) =>
        Task.FromResult(_projects.FirstOrDefault(p => p.Id == id));
    public Task<Project> AddAsync(Project project) => throw new NotSupportedException();
    public Task UpdateAsync(Project project) => Task.CompletedTask;
}

internal sealed class FakeProjectTeamService : ITeamService
{
    private readonly string[] _projectIds;

    public FakeProjectTeamService(string[] projectIds) => _projectIds = projectIds;

    public Task<IReadOnlyList<string>> GetProjectIdsForMemberAsync(string employeeId) =>
        Task.FromResult<IReadOnlyList<string>>(_projectIds);

    public Task<IEnumerable<TeamDto>> GetAllAsync() => throw new NotSupportedException();
    public Task<TeamSaveResult> CreateAsync(CreateTeamDto dto) => throw new NotSupportedException();
    public Task<TeamSaveResult> UpdateAsync(string id, SaveTeamDto dto) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(string id) => throw new NotSupportedException();
    public Task<TeamSaveResult> AddOrUpdateMemberAsync(string teamId, SaveMembershipDto dto) =>
        throw new NotSupportedException();
    public Task<TeamSaveResult> RemoveMemberAsync(string teamId, string employeeId) =>
        throw new NotSupportedException();
    public Task<IEnumerable<ApprovalStepDto>> GetApprovalChainAsync(
        string employeeId, ApprovalModule module, string? projectId = null) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetMemberEmployeeIdsAsync(string teamId) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<SupervisedTeamDto>> GetSupervisedTeamsAsync(string userId) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<string>> GetReportEmployeeIdsAsync(string supervisorId) =>
        throw new NotSupportedException();
    public Task<IReadOnlyList<LayerApproverOptionsDto>?> GetApproverOptionsAsync(string teamId, string employeeId) =>
        throw new NotSupportedException();
    public Task<ApproverOverrideResult> SetApproverOverrideAsync(
        string teamId, string employeeId, int layer, List<string> approverIds) =>
        throw new NotSupportedException();
    public Task<ApproverOverrideResult> ClearApproverOverrideAsync(string teamId, string employeeId, int layer) =>
        throw new NotSupportedException();
}
