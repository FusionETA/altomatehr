using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Projects;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Entities;

namespace AltomateHR.Api.Tests.Teams;

// Admins are oversight, not links in the chain of command.
//
// An Admin/Owner may SEE anything in the org, and approves nothing: never an
// approver in a team chain, never occupying a layer. The consequence these
// tests exist to pin down is that "nobody above me" becomes a REACHABLE
// state — remove the admin from a team and the person at the top has zero
// approval steps, which callers must handle at submit rather than by handing
// the admin approval power back.
public class ApprovalRoutingTests
{
    private const string Team = "team-1";

    [Fact]
    public async Task AnAdminSittingInATeam_IsNotAnApprover()
    {
        // staff → lead → admin. Without the rule, the lead's own requests would
        // route to the admin.
        var router = Build(
            layers: new() { ["staff"] = 0, ["lead"] = 1, ["boss"] = 2 },
            layerCount: 3,
            administrative: ["boss"]);

        Assert.Equal(0, await router.StepCountAsync(ApprovalModule.ATTENDANCE, "lead"));
        Assert.Empty(await router.CurrentApproversAsync(ApprovalModule.ATTENDANCE, "lead", 0));
    }

    [Fact]
    public async Task NonAdminLayersAboveStillApprove()
    {
        // The same shape with a non-admin at the top: the chain is intact, so
        // the exclusion is aimed at the role, not at top layers generally.
        var router = Build(
            layers: new() { ["staff"] = 0, ["lead"] = 1, ["boss"] = 2 },
            layerCount: 3,
            administrative: []);

        Assert.Equal(1, await router.StepCountAsync(ApprovalModule.ATTENDANCE, "lead"));
        Assert.Equal(["boss"], await router.CurrentApproversAsync(ApprovalModule.ATTENDANCE, "lead", 0));
    }

    [Fact]
    public async Task AnEmployeeUnderANonAdminSupervisor_IsUnaffected()
    {
        // The ordinary case must not regress: an admin two layers up is skipped,
        // but the supervisor in between still approves.
        var router = Build(
            layers: new() { ["staff"] = 0, ["lead"] = 1, ["boss"] = 2 },
            layerCount: 3,
            administrative: ["boss"]);

        Assert.Equal(1, await router.StepCountAsync(ApprovalModule.ATTENDANCE, "staff"));
        Assert.Equal(["lead"], await router.CurrentApproversAsync(ApprovalModule.ATTENDANCE, "staff", 0));
    }

    // --- what happens when a layer is removed underneath an in-flight request ---

    [Fact]
    public async Task RemovingAMiddleLayer_AdvancesToTheNextOneUp()
    {
        // staff → lead → manager → director. A request that the lead approved
        // sits at step 1, the manager. Pull the manager out and step 1 must
        // resolve to the DIRECTOR, not to nothing: the chain is rebuilt dense on
        // every read, so the steps re-index and the request moves up.
        var withManager = Build(
            layers: new() { ["staff"] = 0, ["lead"] = 1, ["manager"] = 2, ["director"] = 3 },
            layerCount: 4,
            administrative: []);
        Assert.Equal(["manager"], await withManager.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 1));

        var managerGone = Build(
            layers: new() { ["staff"] = 0, ["lead"] = 1, ["director"] = 3 },
            layerCount: 4,
            administrative: []);

        Assert.Equal(["director"], await managerGone.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 1));
        Assert.Equal(2, await managerGone.StepCountAsync(ApprovalModule.CLAIMS, "staff"));
    }

    [Fact]
    public async Task RemovingTheLastLayer_LeavesTheRequestPastTheEnd()
    {
        // Same shape, but it's the top that goes. A request at step 1 is now
        // beyond the last surviving layer, and every layer that was going to
        // review it is gone — so there is nobody to route to, which is what the
        // callers turn into "approved".
        var router = Build(
            layers: new() { ["staff"] = 0, ["lead"] = 1 },
            layerCount: 4,
            administrative: []);

        Assert.Equal(1, await router.StepCountAsync(ApprovalModule.CLAIMS, "staff"));
        Assert.Empty(await router.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 1));
    }

    [Fact]
    public async Task AnEmptyLayerIsSkipped_NotTreatedAsAStepWithNobodyInIt()
    {
        // The reason the middle case works at all: a vacant layer never becomes
        // a step, so it can't strand anything by sitting in the chain empty.
        var router = Build(
            layers: new() { ["staff"] = 0, ["director"] = 3 },
            layerCount: 4,
            administrative: []);

        Assert.Equal(1, await router.StepCountAsync(ApprovalModule.CLAIMS, "staff"));
        Assert.Equal(["director"], await router.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 0));
    }

    [Fact]
    public async Task Layer0InTheModuleConfig_ChangesNothing()
    {
        // The chain starts at membership.Layer + 1, so the bottom layer is never
        // iterated for anyone — nobody sits below it. Whether layer 0 is ticked
        // in the module config is therefore invisible to routing.
        var withLayer0 = Build(
            layers: new() { ["staff"] = 0, ["lead"] = 1 },
            layerCount: 2,
            administrative: [],
            claimsLayers: [0, 1]);
        var withoutLayer0 = Build(
            layers: new() { ["staff"] = 0, ["lead"] = 1 },
            layerCount: 2,
            administrative: [],
            claimsLayers: [1]);

        Assert.Equal(
            await withLayer0.StepCountAsync(ApprovalModule.CLAIMS, "staff"),
            await withoutLayer0.StepCountAsync(ApprovalModule.CLAIMS, "staff"));
        Assert.Equal(
            await withLayer0.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 0),
            await withoutLayer0.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 0));
    }

    // --- explicit per-employee approver overrides ---

    [Fact]
    public async Task ExplicitOverride_ReplacesTheDefaultApprovers()
    {
        // staff, emma → layer 0; ben, cathy → layer 1. Without an override,
        // staff's layer-1 step would be both ben and cathy (the implicit
        // default). An override naming only ben should narrow it to just him.
        var router = Build(
            layers: new() { ["staff"] = 0, ["emma"] = 0, ["ben"] = 1, ["cathy"] = 1 },
            layerCount: 2,
            administrative: [],
            overrides: [new TeamApprovalOverride { TeamId = Team, EmployeeId = "staff", Layer = 1, ApproverIdsJson = "[\"ben\"]" }]);

        Assert.Equal(1, await router.StepCountAsync(ApprovalModule.CLAIMS, "staff"));
        Assert.Equal(["ben"], await router.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 0));
    }

    [Fact]
    public async Task ExplicitEmptyOverride_SkipsTheLayerForThisEmployeeOnly()
    {
        // Same shape, but staff's override deliberately names nobody — their
        // layer-1 step should vanish entirely (not "a step with zero
        // approvers"), while emma — no override configured for her — still
        // gets the normal implicit default at the very same layer.
        var overrides = new List<TeamApprovalOverride>
        {
            new() { TeamId = Team, EmployeeId = "staff", Layer = 1, ApproverIdsJson = "[]" },
        };
        var router = Build(
            layers: new() { ["staff"] = 0, ["emma"] = 0, ["ben"] = 1, ["cathy"] = 1 },
            layerCount: 2,
            administrative: [],
            overrides: overrides);

        Assert.Equal(0, await router.StepCountAsync(ApprovalModule.CLAIMS, "staff"));
        Assert.Empty(await router.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 0));

        Assert.Equal(1, await router.StepCountAsync(ApprovalModule.CLAIMS, "emma"));
        Assert.Equal(["ben", "cathy"], await router.CurrentApproversAsync(ApprovalModule.CLAIMS, "emma", 0));
    }

    // --- multi-team, project-aware resolution ---

    [Fact]
    public async Task MultiTeamWithProjectId_RoutesThroughTheMatchingProjectsTeam()
    {
        // staff is on two teams at once: Team Alpha (project "Alpha Project",
        // approver alice) and Team Beta (project "Beta Project", approver
        // bob). A CLAIMS request tagged with Beta's project id must resolve
        // through Beta's chain, not Alpha's — and vice versa.
        var (chain, _) = BuildMultiTeam();
        var router = new ApprovalRouter(chain);

        Assert.Equal(["bob"], await router.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 0, "proj-beta"));
        Assert.Equal(["alice"], await router.CurrentApproversAsync(ApprovalModule.CLAIMS, "staff", 0, "proj-alpha"));
    }

    [Fact]
    public async Task MultiTeamNoProjectId_FallsBackToAlphabeticallyFirstProjectName()
    {
        // Same two-team shape, but no projectId given — exactly Leave's case,
        // since it has no project concept at all. Resolution falls back to
        // whichever team's PROJECT NAME sorts first alphabetically
        // ("Alpha Project" before "Beta Project"), not team id order — Team
        // Beta's id ("team-beta") would sort before Team Alpha's
        // ("team-alpha") were it going by id, so this also proves the rule
        // really is project-name-based.
        var (chain, _) = BuildMultiTeam();
        var router = new ApprovalRouter(chain);

        Assert.Equal(["alice"], await router.CurrentApproversAsync(ApprovalModule.LEAVE, "staff", 0));
    }

    private static (ApprovalChainService Chain, List<Team> Teams) BuildMultiTeam()
    {
        var teamAlpha = new Team { Id = "team-alpha", ProjectId = "proj-alpha", LayerCount = 2, LayerLabels = "[]" };
        var teamBeta = new Team { Id = "team-beta", ProjectId = "proj-beta", LayerCount = 2, LayerLabels = "[]" };
        var memberships = new List<TeamMembership>
        {
            new() { TeamId = "team-alpha", EmployeeId = "staff", Layer = 0 },
            new() { TeamId = "team-alpha", EmployeeId = "alice", Layer = 1 },
            new() { TeamId = "team-beta", EmployeeId = "staff", Layer = 0 },
            new() { TeamId = "team-beta", EmployeeId = "bob", Layer = 1 },
        };
        var projects = new List<Project>
        {
            new() { Id = "proj-alpha", Name = "Alpha Project" },
            new() { Id = "proj-beta", Name = "Beta Project" },
        };
        var chain = new ApprovalChainService(
            new StubTeams(teamAlpha, teamBeta),
            new StubTeamMemberships(memberships),
            new StubSupervision([]),
            new StubTeamApprovalOverrides([]),
            new StubProjects(projects));
        return (chain, [teamAlpha, teamBeta]);
    }

    [Fact]
    public async Task BatchedApprovers_MatchAskingOneAtATime()
    {
        // The batch path exists purely for speed — resolving one chain touches
        // five tables, so a queue of N pending requests used to cost N times
        // that. It must therefore agree with the single-request path for every
        // shape that matters, or the optimisation changes who can approve.
        //
        // Covered here: an ordinary employee, someone under an admin (excluded),
        // the top of the chain (no approvers), a step past the end, and an
        // employee on no team at all.
        var router = Build(
            layers: new() { ["staff"] = 0, ["lead"] = 1, ["boss"] = 2 },
            layerCount: 3,
            administrative: ["boss"]);

        (string, string?, int)[] cases =
        [
            ("staff", null, 0),
            ("staff", null, 1),
            ("staff", null, 5),      // past the end of the chain
            ("lead", null, 0),       // only an admin above → no approvers
            ("boss", null, 0),       // top
            ("nobody", null, 0),     // on no team
        ];

        var batched = await router.CurrentApproversForManyAsync(ApprovalModule.CLAIMS, cases);

        foreach (var (applicant, project, step) in cases)
        {
            var one = await router.CurrentApproversAsync(ApprovalModule.CLAIMS, applicant, step, project);
            Assert.Equal(one, batched[(applicant, project, step)]);
        }
    }

    private static ApprovalRouter Build(
        Dictionary<string, int> layers,
        int layerCount,
        string[] administrative,
        int[]? claimsLayers = null,
        List<TeamApprovalOverride>? overrides = null)
    {
        var team = new Team
        {
            Id = Team,
            LayerCount = layerCount,
            LayerLabels = "[]",
            ModuleApprovalConfig = claimsLayers is null
                ? "{}"
                : $"{{\"CLAIMS\":[{string.Join(',', claimsLayers)}]}}",
        };
        var memberships = layers
            .Select(kv => new TeamMembership { TeamId = Team, EmployeeId = kv.Key, Layer = kv.Value })
            .ToList();
        var chain = new ApprovalChainService(
            new StubTeams(team),
            new StubTeamMemberships(memberships),
            new StubSupervision(administrative.ToHashSet()),
            new StubTeamApprovalOverrides(overrides ?? []),
            new StubProjects([]));
        return new ApprovalRouter(chain);
    }

    private sealed class StubTeams(params Team[] teams) : ITeamRepository
    {
        public Task<Team?> GetByIdAsync(string id) => Task.FromResult(teams.FirstOrDefault(t => t.Id == id));
        public Task<List<Team>> GetAllAsync() => Task.FromResult(teams.ToList());
        public Task<Team?> GetByProjectAndNameAsync(string projectId, string name) => Task.FromResult<Team?>(null);
        public Task<Team> AddAsync(Team t) => Task.FromResult(t);
        public Task UpdateAsync(Team t) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    private sealed class StubProjects(List<Project> projects) : IProjectRepository
    {
        public Task<List<Project>> GetAllAsync() => Task.FromResult(projects);
        public Task<Project?> GetByIdAsync(string id) => Task.FromResult(projects.FirstOrDefault(p => p.Id == id));
        public Task<Project> AddAsync(Project p) => Task.FromResult(p);
        public Task UpdateAsync(Project p) => Task.CompletedTask;
    }

    private sealed class StubTeamMemberships(List<TeamMembership> rows) : ITeamMembershipRepository
    {
        public Task<List<TeamMembership>> GetAllAsync() => Task.FromResult(rows);
        public Task<List<TeamMembership>> GetByTeamAsync(string teamId) =>
            Task.FromResult(rows.Where(m => m.TeamId == teamId).ToList());
        public Task<List<TeamMembership>> GetByEmployeeAsync(string employeeId) =>
            Task.FromResult(rows.Where(m => m.EmployeeId == employeeId).ToList());
        public Task<TeamMembership?> GetByTeamAndEmployeeAsync(string teamId, string employeeId) =>
            Task.FromResult(rows.FirstOrDefault(m => m.TeamId == teamId && m.EmployeeId == employeeId));
        public Task AddAsync(TeamMembership m) => Task.CompletedTask;
        public Task UpdateAsync(TeamMembership m) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
        public Task DeleteByTeamAsync(string teamId) => Task.CompletedTask;
    }

    private sealed class StubTeamApprovalOverrides(List<TeamApprovalOverride> rows) : ITeamApprovalOverrideRepository
    {
        public Task<List<TeamApprovalOverride>> GetAllAsync() => Task.FromResult(rows.ToList());
        public Task<List<TeamApprovalOverride>> GetByTeamAndEmployeeAsync(string teamId, string employeeId) =>
            Task.FromResult(rows.Where(o => o.TeamId == teamId && o.EmployeeId == employeeId).ToList());
        public Task<TeamApprovalOverride?> GetAsync(string teamId, string employeeId, int layer) =>
            Task.FromResult(rows.FirstOrDefault(o => o.TeamId == teamId && o.EmployeeId == employeeId && o.Layer == layer));
        public Task UpsertAsync(TeamApprovalOverride ov) => Task.CompletedTask;
        public Task DeleteAsync(string teamId, string employeeId, int layer) => Task.CompletedTask;
        public Task DeleteByTeamAsync(string teamId) => Task.CompletedTask;
        public Task DeleteByTeamAndEmployeeAsync(string teamId, string employeeId) => Task.CompletedTask;
        public Task DeleteLayersAboveAsync(string teamId, int maxLayer) => Task.CompletedTask;
    }

    private sealed class StubSupervision(HashSet<string> administrative) : ISupervisionService
    {
        public Task<IReadOnlySet<string>> GetAdministrativeUserIdsAsync() =>
            Task.FromResult<IReadOnlySet<string>>(administrative);

        public Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IEnumerable<string> userIds) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public bool IsOrgApprover(string? role) => role is "Admin" or "Owner";
    }
}
