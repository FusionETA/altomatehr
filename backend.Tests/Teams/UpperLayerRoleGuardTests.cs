using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Audit.Dtos;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Dtos;
using AltomateHR.Api.Modules.Teams.Entities;

namespace AltomateHR.Api.Tests.Teams;

// A layer above the bottom IS an approver position: every request from below
// routes to whoever stands there. The approvals screens are gated on the
// stored role, so the two have to agree — a plain Employee up there is a dead
// end where requests route to someone the app then refuses to admit.
//
// v1 never hit this because one form saved role and team placement together.
// v2 split them across two screens, so the rule is enforced here instead.
public class UpperLayerRoleGuardTests
{
    private const string Team = "team-1";

    private static (TeamService Service, List<TeamMembership> Rows) Make(
        string role, int layerCount = 2)
    {
        var rows = new List<TeamMembership>();
        var teams = new StubTeams(new Team
        {
            Id = Team, OrganizationId = "org-1", ProjectId = "proj-1",
            Name = "Ops", LayerCount = layerCount,
        });
        var memberships = new StubMemberships(rows);
        var supervision = new StubSupervision(role);

        return (new TeamService(
            teams, memberships, supervision,
            new StubChain(), new StubOverrides(), new StubAudit()), rows);
    }

    private static SaveMembershipDto At(int layer) =>
        new() { EmployeeId = "usr-1", Layer = layer };

    // The bottom layer approves nobody, so any role belongs there.
    [Theory]
    [InlineData("Employee")]
    [InlineData("Supervisor")]
    public async Task AnyoneMayStandAtTheBottomLayer(string role)
    {
        var (service, rows) = Make(role);

        var result = await service.AddOrUpdateMemberAsync(Team, At(0));

        Assert.True(result.Ok, result.Error);
        Assert.Single(rows);
    }

    [Fact]
    public async Task AnEmployeeIsRefusedAnUpperLayer()
    {
        var (service, rows) = Make("Employee");

        var result = await service.AddOrUpdateMemberAsync(Team, At(1));

        Assert.False(result.Ok);
        Assert.Contains("Supervisor", result.Error!);
        // Refused means NOT WRITTEN — a guard that reports an error and saves
        // anyway is worse than none, because the screen says it failed.
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ASupervisorMayStandAtAnUpperLayer()
    {
        var (service, rows) = Make("Supervisor");

        var result = await service.AddOrUpdateMemberAsync(Team, At(1));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1, rows.Single().Layer);
    }

    // Approval routing subtracts administrative seats outright, so an admin in
    // a team is never anybody's approver whatever layer they sit in. Refusing
    // them here would block a placement that cannot cause the problem.
    [Theory]
    [InlineData("Admin")]
    [InlineData("Owner")]
    public async Task AnAdministrativeSeatIsNotSubjectToTheRule(string role)
    {
        var (service, _) = Make(role);

        var result = await service.AddOrUpdateMemberAsync(Team, At(1));

        Assert.True(result.Ok, result.Error);
    }

    // The guard has to cover MOVES, not just first placement — otherwise it is
    // bypassed by adding someone at the bottom and promoting them after.
    [Fact]
    public async Task AnEmployeeCannotBeMovedUpAfterBeingAddedAtTheBottom()
    {
        var (service, rows) = Make("Employee");
        Assert.True((await service.AddOrUpdateMemberAsync(Team, At(0))).Ok);

        var result = await service.AddOrUpdateMemberAsync(Team, At(1));

        Assert.False(result.Ok);
        Assert.Equal(0, rows.Single().Layer);
    }

    // ─── The position lookup the demotion guard reads ───────────────────

    [Fact]
    public async Task ApproverPositionsReportsOnlyLayersAboveTheBottom()
    {
        var rows = new List<TeamMembership>
        {
            new() { TeamId = "t-a", EmployeeId = "usr-1", Layer = 0 },
            new() { TeamId = "t-b", EmployeeId = "usr-1", Layer = 2 },
            new() { TeamId = "t-c", EmployeeId = "usr-2", Layer = 1 },
        };

        var positions = new ApproverPositions(
            new StubMemberships(rows),
            new StubTeams(
                new Team { Id = "t-a", Name = "Alpha" },
                new Team { Id = "t-b", Name = "Bravo" }));

        var found = await positions.ForEmployeeAsync("usr-1");

        var one = Assert.Single(found);
        Assert.Equal("Bravo", one.TeamName);
        Assert.Equal(2, one.Layer);
    }

    // Judged by the position, not by whether anyone stands below right now. A
    // rule that flickered as the bottom layer filled and emptied would let a
    // placement become invalid with no admin action to blame it on.
    [Fact]
    public async Task AnUpperLayerCountsEvenWithTheLayerBelowEmpty()
    {
        var positions = new ApproverPositions(
            new StubMemberships([new TeamMembership { TeamId = "t-a", EmployeeId = "usr-1", Layer = 1 }]),
            new StubTeams(new Team { Id = "t-a", Name = "Alpha" }));

        Assert.Single(await positions.ForEmployeeAsync("usr-1"));
    }

    // ─── Stubs ──────────────────────────────────────────────────────────

    private sealed class StubTeams(params Team[] teams) : ITeamRepository
    {
        public Task<List<Team>> GetAllAsync() => Task.FromResult(teams.ToList());
        public Task<Team?> GetByIdAsync(string id) =>
            Task.FromResult(teams.FirstOrDefault(t => t.Id == id));
        public Task<Team?> GetByProjectAndNameAsync(string projectId, string name) =>
            Task.FromResult<Team?>(null);
        public Task<Team> AddAsync(Team team) => Task.FromResult(team);
        public Task UpdateAsync(Team team) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
    }

    private sealed class StubMemberships(List<TeamMembership> rows) : ITeamMembershipRepository
    {
        public Task<List<TeamMembership>> GetAllAsync() => Task.FromResult(rows);
        public Task<List<TeamMembership>> GetByTeamAsync(string teamId) =>
            Task.FromResult(rows.Where(r => r.TeamId == teamId).ToList());
        public Task<List<TeamMembership>> GetByEmployeeAsync(string employeeId) =>
            Task.FromResult(rows.Where(r => r.EmployeeId == employeeId).ToList());
        public Task<TeamMembership?> GetByTeamAndEmployeeAsync(string teamId, string employeeId) =>
            Task.FromResult(rows.FirstOrDefault(r => r.TeamId == teamId && r.EmployeeId == employeeId));
        public Task AddAsync(TeamMembership m) { rows.Add(m); return Task.CompletedTask; }
        public Task UpdateAsync(TeamMembership m) => Task.CompletedTask;
        public Task DeleteAsync(string id) => Task.CompletedTask;
        public Task DeleteByTeamAsync(string teamId) => Task.CompletedTask;
    }

    private sealed class StubOverrides : ITeamApprovalOverrideRepository
    {
        public Task<List<TeamApprovalOverride>> GetAllAsync() => Task.FromResult(new List<TeamApprovalOverride>());
        public Task<List<TeamApprovalOverride>> GetByTeamAndEmployeeAsync(string teamId, string employeeId) =>
            Task.FromResult(new List<TeamApprovalOverride>());
        public Task<TeamApprovalOverride?> GetAsync(string teamId, string employeeId, int layer) =>
            Task.FromResult<TeamApprovalOverride?>(null);
        public Task UpsertAsync(TeamApprovalOverride ov) => Task.CompletedTask;
        public Task DeleteAsync(string teamId, string employeeId, int layer) => Task.CompletedTask;
        public Task DeleteByTeamAsync(string teamId) => Task.CompletedTask;
        public Task DeleteByTeamAndEmployeeAsync(string teamId, string employeeId) => Task.CompletedTask;
        public Task DeleteLayersAboveAsync(string teamId, int maxLayer) => Task.CompletedTask;
    }

    private sealed class StubChain : IApprovalChainService
    {
        public Task<IReadOnlyList<ApprovalStep>> GetChainAsync(
            string employeeId, ApprovalModule module, string? projectId = null) =>
            Task.FromResult<IReadOnlyList<ApprovalStep>>([]);

        public Task<IReadOnlyDictionary<(string EmployeeId, string? ProjectId), IReadOnlyList<ApprovalStep>>>
            GetChainsAsync(IReadOnlyCollection<(string EmployeeId, string? ProjectId)> keys, ApprovalModule module) =>
            Task.FromResult<IReadOnlyDictionary<(string, string?), IReadOnlyList<ApprovalStep>>>(
                new Dictionary<(string, string?), IReadOnlyList<ApprovalStep>>());
    }

    private sealed class StubSupervision(string role) : ISupervisionService
    {
        public Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IEnumerable<string> userIds) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                userIds.ToDictionary(id => id, _ => "person@example.com"));

        public Task<IReadOnlyDictionary<string, string>> GetNamesAsync(IEnumerable<string> userIds) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public bool IsOrgApprover(string? r) => r is "Admin" or "Owner";

        public Task<IReadOnlySet<string>> GetAdministrativeUserIdsAsync() =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

        public Task<string?> GetRoleAsync(string userId) => Task.FromResult<string?>(role);
    }

    private sealed class StubAudit : IAuditService
    {
        public Task WriteAsync(AuditEvent entry) => Task.CompletedTask;
        public Task<AuditPageDto> ListAsync(AuditQueryDto query) => throw new NotSupportedException();
        public Task<AuditVerificationDto> VerifyAsync() => throw new NotSupportedException();
    }
}
