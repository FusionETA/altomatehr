using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Audit.Dtos;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Teams.Dtos;
using AltomateHR.Api.Modules.Teams.Entities;

namespace AltomateHR.Api.Tests.Teams;

// An admin limited to some policies builds and runs teams for their own
// people, but can't re-route approvals for staff outside those policies.
public class LimitedAdminTeamTests
{
    private const string TeamId = "team-1";
    private const string Mine = "usr-mine";       // on a policy the admin covers
    private const string NotMine = "usr-other";   // on a policy they don't
    private const string Me = "usr-me";           // the limited admin
    private const string OtherAdmin = "usr-admin";

    private static (TeamService Service, List<TeamMembership> Rows, List<string> Deleted) Make(
        bool limited, params (string Id, int Layer)[] members)
    {
        var rows = members
            .Select(m => new TeamMembership { TeamId = TeamId, EmployeeId = m.Id, Layer = m.Layer })
            .ToList();
        var deleted = new List<string>();
        var scope = new EmployeeScope();
        if (limited) scope.Limit([Me, Mine], []);

        var service = new TeamService(
            new StubTeams(deleted), new StubMemberships(rows), new StubSupervision(),
            new StubChain(), new StubOverrides(), new StubAudit(), scope);
        return (service, rows, deleted);
    }

    private static SaveTeamDto Edit() => new() { Name = "FD", LayerCount = 2, LayerLabels = ["Staff", "Lead"] };

    [Fact]
    public async Task ALimitedAdminCanCreateATeam()
    {
        var (service, _, _) = Make(limited: true);

        var result = await service.CreateAsync(new CreateTeamDto
        {
            ProjectId = "proj-1", Name = "FD", LayerCount = 1, LayerLabels = ["Staff"],
        });

        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public async Task ALimitedAdminCanRunATeamOfTheirOwnPeople()
    {
        // Being on the team themselves doesn't count against them.
        var (service, rows, deleted) = Make(limited: true, (Me, 1));

        Assert.True((await service.AddOrUpdateMemberAsync(TeamId, new() { EmployeeId = Mine, Layer = 0 })).Ok);
        Assert.True((await service.UpdateAsync(TeamId, Edit())).Ok);
        Assert.True((await service.RemoveMemberAsync(TeamId, Mine)).Ok);
        Assert.True((await service.DeleteAsync(TeamId)).Ok);
        Assert.Equal([TeamId], deleted);
        Assert.Equal(2, rows.Count);   // the stub keeps rows; removal is the repo's job
    }

    [Fact]
    public async Task ALimitedAdminCannotAddSomeoneOutsideTheirPolicies()
    {
        var (service, rows, _) = Make(limited: true);

        var result = await service.AddOrUpdateMemberAsync(TeamId, new() { EmployeeId = NotMine, Layer = 0 });

        Assert.False(result.Ok);
        Assert.Contains("isn't on the policies", result.Error!);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task ALimitedAdminCannotChangeATeamHoldingOtherPeople()
    {
        var (service, rows, deleted) = Make(limited: true, (Mine, 0), (NotMine, 0));

        var update = await service.UpdateAsync(TeamId, Edit());
        var add = await service.AddOrUpdateMemberAsync(TeamId, new() { EmployeeId = Mine, Layer = 0 });
        var delete = await service.DeleteAsync(TeamId);
        var approvers = await service.SetApproverOverrideAsync(TeamId, Mine, 1, []);

        Assert.All([update.Error, add.Error, delete.Error, approvers.Error],
            e => Assert.Contains("1 person outside the policies", e!));
        Assert.Empty(deleted);
        Assert.Equal(2, rows.Count);
    }

    // Another admin's own leave and claims go up the team's chain too, and the
    // clean-up after an edit can't see their requests from a limited scope.
    [Fact]
    public async Task ALimitedAdminCannotChangeATeamHoldingAnotherAdmin()
    {
        var (service, _, deleted) = Make(limited: true, (OtherAdmin, 0), (Mine, 1));

        var remove = await service.RemoveMemberAsync(TeamId, Mine);
        var delete = await service.DeleteAsync(TeamId);

        Assert.Contains("1 person outside the policies", remove.Error!);
        Assert.False(delete.Ok);
        Assert.Empty(deleted);
    }

    [Fact]
    public async Task AnAdminWithEveryEmployeeIsNotHeldBack()
    {
        var (service, _, deleted) = Make(limited: false, (NotMine, 0));

        Assert.True((await service.AddOrUpdateMemberAsync(TeamId, new() { EmployeeId = Mine, Layer = 0 })).Ok);
        Assert.True((await service.DeleteAsync(TeamId)).Ok);
        Assert.Equal([TeamId], deleted);
    }

    // ─── Stubs ──────────────────────────────────────────────────────────

    private sealed class StubTeams(List<string> deleted) : ITeamRepository
    {
        private readonly Team _team = new() { Id = TeamId, ProjectId = "proj-1", Name = "FD", LayerCount = 2 };
        public Task<List<Team>> GetAllAsync() => Task.FromResult(new List<Team> { _team });
        public Task<Team?> GetByIdAsync(string id) => Task.FromResult(id == TeamId ? _team : null);
        public Task<Team?> GetByProjectAndNameAsync(string projectId, string name) => Task.FromResult<Team?>(null);
        public Task<Team> AddAsync(Team team) => Task.FromResult(team);
        public Task UpdateAsync(Team team) => Task.CompletedTask;
        public Task DeleteAsync(string id) { deleted.Add(id); return Task.CompletedTask; }
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

    private sealed class StubSupervision : ISupervisionService
    {
        public Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IEnumerable<string> userIds) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(
                userIds.ToDictionary(id => id, _ => "person@example.com"));

        public Task<IReadOnlyDictionary<string, string>> GetNamesAsync(IEnumerable<string> userIds) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public bool IsOrgApprover(string? r) => r is "Admin" or "Owner";

        public Task<IReadOnlySet<string>> GetAdministrativeUserIdsAsync() =>
            Task.FromResult<IReadOnlySet<string>>(new HashSet<string> { OtherAdmin });

        public Task<string?> GetRoleAsync(string userId) =>
            Task.FromResult<string?>(userId == OtherAdmin ? "Admin" : "Employee");
    }

    private sealed class StubAudit : IAuditService
    {
        public Task WriteAsync(AuditEvent entry) => Task.CompletedTask;
        public Task<AuditPageDto> ListAsync(AuditQueryDto query) => throw new NotSupportedException();
        public Task<AuditVerificationDto> VerifyAsync() => throw new NotSupportedException();
    }
}
