using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Dtos;

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

    // ---- wiring ----

    private static Project Project(string id, string name) => new() { Id = id, Name = name };

    private static Project Archived(string id, string name) =>
        new() { Id = id, Name = name, IsArchived = true };

    private static ProjectService Create(List<Project> projects, string[]? onProjects = null) =>
        new(new FakeProjectRepository(projects),
            new FakeAuditService(),
            new FakeProjectTeamService(onProjects ?? []));
}

internal sealed class FakeProjectRepository : IProjectRepository
{
        public Task<List<ProjectGeofencePoint>> GetGeofencePointsAsync(string projectId) =>
            Task.FromResult(new List<ProjectGeofencePoint>());
        public Task<List<ProjectAllowedIp>> GetAllowedIpsAsync(string projectId) =>
            Task.FromResult(new List<ProjectAllowedIp>());
        public Task ReplaceGeofencePointsAsync(string projectId, IReadOnlyList<ProjectGeofencePoint> points) =>
            Task.CompletedTask;
        public Task ReplaceAllowedIpsAsync(string projectId, IReadOnlyList<ProjectAllowedIp> entries) =>
            Task.CompletedTask;
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
