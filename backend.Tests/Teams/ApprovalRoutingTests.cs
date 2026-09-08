using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Entities;

namespace AltomateHR.Api.Tests.Teams;

// Admins are oversight, not links in the chain of command.
//
// An Admin/Owner may SEE anything in the org, and approves nothing: never an
// approver in a team chain, never the supervisor fallback, never occupying a
// layer. The consequence these tests exist to pin down is that "nobody above
// me" becomes a REACHABLE state — remove the admin from a team and the person
// at the top has zero approval steps, which callers must handle at submit
// rather than by handing the admin approval power back.
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

    [Fact]
    public async Task AnAdminAssignedAsSomeonesSupervisor_IsNotTheirApprover()
    {
        // The flat fallback, used when the employee has no team. Being recorded
        // as someone's supervisor is an org-chart fact; it doesn't make an admin
        // an approver.
        var router = Build(
            layers: [],
            layerCount: 1,
            administrative: ["boss"],
            supervisorOf: new() { ["staff"] = "boss" });

        Assert.Equal(0, await router.StepCountAsync(ApprovalModule.LEAVE, "staff"));
        Assert.Empty(await router.CurrentApproversAsync(ApprovalModule.LEAVE, "staff", 0));
    }

    [Fact]
    public async Task ANonAdminSupervisor_IsStillTheFallbackApprover()
    {
        var router = Build(
            layers: [],
            layerCount: 1,
            administrative: ["boss"],
            supervisorOf: new() { ["staff"] = "lead" });

        Assert.Equal(1, await router.StepCountAsync(ApprovalModule.LEAVE, "staff"));
        Assert.Equal(["lead"], await router.CurrentApproversAsync(ApprovalModule.LEAVE, "staff", 0));
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

    private static ApprovalRouter Build(
        Dictionary<string, int> layers,
        int layerCount,
        string[] administrative,
        Dictionary<string, string>? supervisorOf = null,
        int[]? claimsLayers = null)
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
        var supervision = new StubSupervision(administrative.ToHashSet(), supervisorOf ?? []);
        var chain = new ApprovalChainService(new StubTeams(team), new StubTeamMemberships(memberships), supervision);
        return new ApprovalRouter(chain, supervision);
    }

    private sealed class StubTeams(Team team) : ITeamRepository
    {
        public Task<Team?> GetByIdAsync(string id) => Task.FromResult<Team?>(team.Id == id ? team : null);
        public Task<List<Team>> GetAllAsync() => Task.FromResult<List<Team>>([team]);
        public Task<Team?> GetByProjectAndNameAsync(string projectId, string name) => Task.FromResult<Team?>(null);
        public Task<Team> AddAsync(Team t) => Task.FromResult(t);
        public Task UpdateAsync(Team t) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
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

    private sealed class StubSupervision(HashSet<string> administrative, Dictionary<string, string> supervisorOf)
        : ISupervisionService
    {
        public Task<IReadOnlySet<string>> GetAdministrativeUserIdsAsync() =>
            Task.FromResult<IReadOnlySet<string>>(administrative);

        public Task<string?> GetSupervisorIdAsync(string employeeId) =>
            Task.FromResult(supervisorOf.GetValueOrDefault(employeeId));

        public Task<IReadOnlyList<string>> GetReportIdsAsync(string supervisorId) =>
            Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IEnumerable<string> userIds) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public bool IsOrgApprover(string? role) => role is "Admin" or "Owner";

        public Task<bool> CanApproveAsync(string applicantId, string approverId, string? role) =>
            Task.FromResult(false);
    }
}
