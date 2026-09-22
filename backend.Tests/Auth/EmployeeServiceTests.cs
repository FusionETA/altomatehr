using Microsoft.Extensions.Options;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Email;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;

using AltomateHR.Api.Tests.Audit;

namespace AltomateHR.Api.Tests.Auth;

public class EmployeeServiceTests
{
    [Fact]
    public async Task UpdateAsync_RejectsUnknownRole()
    {
        var service = MakeService(out _);

        var result = await service.UpdateAsync("usr-emp", new UpdateEmployeeDto { Role = "Wizard" });

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    // The other half of the rule Teams enforces on placement. Without it the
    // guard is a formality: place someone correctly as a Supervisor, then
    // demote them here, and they are an approver the approvals screens will
    // not let in — with their team already routing requests to them.
    [Fact]
    public async Task UpdateAsync_RefusesToDemoteSomeoneWhoStillApprovesForATeam()
    {
        var positions = new FakeApproverPositions();
        positions.Place("usr-super", "Ops", layer: 1);
        var service = MakeService(out var memberships, out _, out _, positions);

        var result = await service.UpdateAsync("usr-super", new UpdateEmployeeDto { Role = "Employee" });

        Assert.False(result.Ok);
        Assert.Contains("Ops", result.Error!);
        // Refused means unchanged — not reported-and-saved.
        Assert.Equal("Supervisor", memberships.Single(m => m.UserId == "usr-super").Role);
    }

    [Fact]
    public async Task UpdateAsync_DemotesSomeoneWhoApprovesForNobody()
    {
        var service = MakeService(out var memberships);

        var result = await service.UpdateAsync("usr-super", new UpdateEmployeeDto { Role = "Employee" });

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Employee", memberships.Single(m => m.UserId == "usr-super").Role);
    }

    // Admin and Owner are subtracted from routing outright, so they are never
    // anybody's approver whatever layer they sit in — promoting into one of
    // those seats cannot strand a team.
    [Fact]
    public async Task UpdateAsync_AllowsAnApproverToBecomeAnAdmin()
    {
        var positions = new FakeApproverPositions();
        positions.Place("usr-super", "Ops", layer: 1);
        var service = MakeService(out var memberships, out _, out _, positions);

        var result = await service.UpdateAsync("usr-super", new UpdateEmployeeDto { Role = "Admin" });

        Assert.True(result.Ok, result.Error);
        Assert.Equal("Admin", memberships.Single(m => m.UserId == "usr-super").Role);
    }

    // Data that predates the guard stays editable. Someone already stored as
    // an Employee at an upper layer is a problem to fix on the team screen —
    // blocking every unrelated edit to their name or policy until then would
    // punish the admin for a state they did not create.
    [Fact]
    public async Task UpdateAsync_DoesNotBlockEditingSomeoneAlreadyStoredAsAnEmployee()
    {
        var positions = new FakeApproverPositions();
        positions.Place("usr-emp", "Ops", layer: 1);
        var service = MakeService(out _, out _, out _, positions);

        var result = await service.UpdateAsync("usr-emp", new UpdateEmployeeDto { Role = "Employee" });

        Assert.True(result.Ok, result.Error);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNotFoundResultForNonMember()
    {
        var service = MakeService(out _);

        var result = await service.UpdateAsync("ghost", new UpdateEmployeeDto { Role = "Employee" });

        Assert.False(result.Ok);
        Assert.Null(result.Error);   // no error message → controller maps to 404
    }

    [Fact]
    public async Task UpdateAsync_DoesNotWipeProfileFieldsAnUpdateOmits()
    {
        var service = MakeService(out var memberships);
        var membership = memberships.Single(m => m.UserId == "usr-emp");
        membership.EmployeeNumber = "E-014";
        membership.JobTitle = "Site Engineer";
        membership.JoinDate = new DateTime(2024, 3, 1);

        // What the employees table sends when an admin changes only the role.
        var result = await service.UpdateAsync(
            "usr-emp",
            new UpdateEmployeeDto { Role = "Supervisor" });

        Assert.True(result.Ok);
        // These used to be nulled by any update that omitted them — and losing
        // the join date also re-derived the person's leave accrual.
        Assert.Equal("E-014", membership.EmployeeNumber);
        Assert.Equal("Site Engineer", membership.JobTitle);
        Assert.Equal(new DateTime(2024, 3, 1), membership.JoinDate);
    }

    [Fact]
    public async Task UpdateAsync_ClearsAJobTitleWhenAskedWithAnEmptyString()
    {
        var service = MakeService(out var memberships);
        var membership = memberships.Single(m => m.UserId == "usr-emp");
        membership.JobTitle = "Site Engineer";

        // Empty string is the deliberate "remove it" — distinct from omitting.
        var result = await service.UpdateAsync(
            "usr-emp",
            new UpdateEmployeeDto { Role = "Employee", JobTitle = "" });

        Assert.True(result.Ok);
        Assert.Null(membership.JobTitle);
    }

    // --- helpers ---

    // The join date is stored twice: on the membership, which pro-rates leave,
    // and on the EmployeeProfile, which is what payroll readiness gates on.
    // Writing only the membership left an employee reading "Employment
    // incomplete" however carefully the date was filled in.
    [Fact]
    public async Task CreateAsync_WritesTheJoinDateToTheProfileAsWellAsTheMembership()
    {
        var service = MakeService(out var memberships, out var profiles);

        var result = await service.CreateAsync(new CreateEmployeeDto
        {
            Email = "new@altomate.com",
            Name = "New Person",
            Password = "irrelevant-but-required",
            Role = "Employee",
            JoinDate = new DateTime(2026, 3, 1),
        });

        Assert.True(result.Ok, result.Error);
        Assert.Equal(new DateTime(2026, 3, 1), memberships.Single(m => m.UserId == result.Employee!.Id).JoinDate);
        Assert.Equal(new DateTime(2026, 3, 1), Assert.Single(profiles).JoinDate);
    }

    [Fact]
    public async Task UpdateAsync_WritesTheJoinDateThroughToAnExistingProfile()
    {
        var service = MakeService(out _, out var profiles);
        profiles.Add(new EmployeeProfile { UserId = "usr-emp", JoinDate = new DateTime(2020, 1, 1) });

        var result = await service.UpdateAsync("usr-emp", new UpdateEmployeeDto
        {
            Role = "Employee",
            JoinDate = new DateTime(2026, 3, 1),
        });

        Assert.True(result.Ok, result.Error);
        Assert.Equal(new DateTime(2026, 3, 1), Assert.Single(profiles).JoinDate);
    }

    // Same patch semantics as the membership: omitting the date must not clear
    // one the profile already has.
    [Fact]
    public async Task UpdateAsync_WithoutAJoinDateLeavesTheProfilesAlone()
    {
        var service = MakeService(out _, out var profiles);
        profiles.Add(new EmployeeProfile { UserId = "usr-emp", JoinDate = new DateTime(2020, 1, 1) });

        await service.UpdateAsync("usr-emp", new UpdateEmployeeDto { Role = "Supervisor" });

        Assert.Equal(new DateTime(2020, 1, 1), Assert.Single(profiles).JoinDate);
    }

    // ─── An admin setting someone else's password ───────────────────────
    //
    // The self-service reset mails a code to the address on file, which is no
    // use to a returning employee whose personal address is gone. Each guard
    // below closes one way that door could be misused.

    [Fact]
    public async Task SetPassword_StoresAHashRatherThanThePassword()
    {
        var service = MakeService(out _, out _, out var users);

        var result = await service.SetPasswordAsync("usr-emp", "a-good-password");

        Assert.True(result.Ok);
        var hash = users.Single(u => u.Id == "usr-emp").PasswordHash;
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.NotEqual("a-good-password", hash);
    }

    // Replacing the credential behind your own live session, mid-request.
    [Fact]
    public async Task SetPassword_RefusesTheCallerThemselves()
    {
        var service = MakeService(out _, out _, out _);

        // StubCurrentUser is usr-admin.
        var result = await service.SetPasswordAsync("usr-admin", "a-good-password");

        Assert.False(result.Ok);
        Assert.Contains("your own password", result.Error);
    }

    // An owner's account is the top of the trust chain — resetting it could
    // hand over the company.
    [Fact]
    public async Task SetPassword_RefusesAnOwnerAccount()
    {
        var service = MakeService(out var memberships, out _, out var users);
        memberships.Add(Membership("usr-owner", "Owner"));
        users.Add(User("usr-owner", "owner@altomate.com"));

        var result = await service.SetPasswordAsync("usr-owner", "a-good-password");

        Assert.False(result.Ok);
        Assert.Contains("Owner accounts", result.Error);
    }

    // Not-found rather than a distinct error: an admin of one company must not
    // be able to probe for accounts in another.
    [Fact]
    public async Task SetPassword_TreatsSomeoneOutsideTheOrgAsNotFound()
    {
        var service = MakeService(out _, out _, out _);

        var result = await service.SetPasswordAsync("usr-stranger", "a-good-password");

        Assert.False(result.Ok);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task SetPassword_RefusesAPasswordShorterThanTheResetPolicyAllows()
    {
        var service = MakeService(out _, out _, out var users);

        var result = await service.SetPasswordAsync("usr-emp", "short");

        Assert.False(result.Ok);
        Assert.Contains("at least 8", result.Error);
        // Refused before anything was written.
        Assert.True(string.IsNullOrEmpty(users.Single(u => u.Id == "usr-emp").PasswordHash));
    }

    private static EmployeeService MakeService(out List<OrganizationMembership> memberships) =>
        MakeService(out memberships, out _);

    private static EmployeeService MakeService(
        out List<OrganizationMembership> memberships, out List<EmployeeProfile> profiles) =>
        MakeService(out memberships, out profiles, out _);

    private static EmployeeService MakeService(
        out List<OrganizationMembership> memberships,
        out List<EmployeeProfile> profiles,
        out List<User> users) =>
        MakeService(out memberships, out profiles, out users, new FakeApproverPositions());

    private static EmployeeService MakeService(
        out List<OrganizationMembership> memberships,
        out List<EmployeeProfile> profiles,
        out List<User> users,
        FakeApproverPositions positions)
    {
        users = new List<User>
        {
            User("usr-admin", "admin@altomate.com"),
            User("usr-super", "supervisor@altomate.com"),
            User("usr-emp", "employee@altomate.com"),
        };
        memberships =
        [
            Membership("usr-admin", "Admin"),
            Membership("usr-super", "Supervisor"),
            Membership("usr-emp", "Employee"),
        ];
        profiles = [];
        return new EmployeeService(
            new FakeMembershipRepository(memberships),
            new FakeProfileRepository(profiles),
            new FakeUserRepository(users),
            new FakeLeaveService(),
            new FakeAuditService(),
            new StubCurrentUser(),
            new CapturingEmailSender(),
            new FakeOrgRepositoryForWelcome(),
            positions,
            Options.Create(new PortalOptions()));
    }

    private static User User(string id, string email) => new()
    {
        Id = id,
        Email = email,
        CreatedAt = DateTime.UtcNow,
    };

    private static OrganizationMembership Membership(string userId, string role) => new()
    {
        OrganizationId = "org-1",
        UserId = userId,
        Role = role,
    };

    private sealed class FakeMembershipRepository : IOrganizationMembershipRepository
    {
        private readonly List<OrganizationMembership> _m;
        public FakeMembershipRepository(List<OrganizationMembership> m) => _m = m;

        public Task<List<OrganizationMembership>> GetByUserAsync(string userId) =>
            Task.FromResult(_m.Where(x => x.UserId == userId).ToList());
        public Task<OrganizationMembership?> GetAsync(string organizationId, string userId) =>
            Task.FromResult(_m.FirstOrDefault(x => x.OrganizationId == organizationId && x.UserId == userId));
        public Task<List<OrganizationMembership>> GetForCurrentOrgAsync() => Task.FromResult(_m.ToList());
        public Task<OrganizationMembership?> GetForUserInCurrentOrgAsync(string userId) =>
            Task.FromResult(_m.FirstOrDefault(x => x.UserId == userId));
        public Task<int> CountByShiftIdAsync(string shiftId) =>
            Task.FromResult(_m.Count(x => x.ShiftId == shiftId));
        public Task AddAsync(OrganizationMembership m) { _m.Add(m); return Task.CompletedTask; }
        public Task UpdateAsync(OrganizationMembership m) => Task.CompletedTask;   // service mutates in place
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users;
        public FakeUserRepository(List<User> users) => _users = users;
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult(_users.FirstOrDefault(u => u.Email == email));
        public Task<User?> GetByIdAsync(string id) => Task.FromResult(_users.FirstOrDefault(u => u.Id == id));
        public Task<List<User>> GetAllAsync() => Task.FromResult(_users.ToList());
        public Task AddAsync(User user) { _users.Add(user); return Task.CompletedTask; }
        public Task UpdateAsync(User user) => Task.CompletedTask;
        public Task<bool> AnyAsync() => Task.FromResult(_users.Count > 0);
    }

    // EmployeeService only reaches into leave to recompute pro-rated accrual
    // after a join-date change; nothing else is exercised here.
    private sealed class FakeLeaveService : ILeaveService
    {
        public Task<int> ReconcileUnreachableApprovalsAsync(bool apply) => Task.FromResult(0);

        public Task<IEnumerable<LeaveApplicationDto>> GetAllForOrgAsync() =>
            Task.FromResult<IEnumerable<LeaveApplicationDto>>([]);

        public Task<int> RecomputeProRatedAccrualAsync(string employeeId, int year) => Task.FromResult(0);

        public Task<IEnumerable<LeaveApplicationDto>> GetMineAsync(string u) => throw new NotImplementedException();
        public Task<IEnumerable<LeaveApplicationDto>> GetTeamAsync(string u) => throw new NotImplementedException();
        public Task<IEnumerable<LeaveBalanceDto>> GetBalancesAsync(string e, int y) => throw new NotImplementedException();
        public Task<LeaveBalancesResult> GetBalancesForEmployeeAsync(string e, int y) => throw new NotImplementedException();
        public Task<IEnumerable<EmployeeLeaveBalancesDto>> GetOrgBalancesAsync(int y) => throw new NotImplementedException();
        public Task<LeaveExportResult> ExportBalancesAsync(string e, int y, TabularFormat f) => throw new NotImplementedException();
        public Task<LeaveExportResult> ExportOrgBalancesAsync(int y, TabularFormat f) => throw new NotImplementedException();
        public TabularExportResult BuildImportTemplate(TabularFormat f) => throw new NotImplementedException();
        public Task<TabularImportResult> ImportHistoryAsync(byte[] c, TabularFormat f, string a) => throw new NotImplementedException();
        public Task<LeaveExportResult> ExportBulkSummaryZipAsync(int y, IReadOnlyList<string>? ids) => throw new NotImplementedException();
        public Task<IEnumerable<EmployeeLeaveBalancesDto>> GetTeamBalancesAsync(string s, int y) => throw new NotImplementedException();
        public Task<IEnumerable<OnLeaveTodayDto>> GetOnLeaveTodayAsync(DateTime d) => throw new NotImplementedException();
        public Task<int> CountPendingApprovalsAsync(string r) => throw new NotImplementedException();
        public Task<LeaveEntitlementResult> SetEntitlementAsync(string e, string t, int y, SetEntitlementDto d) => throw new NotImplementedException();
        public Task<LeaveEntitlementResult> ResetEntitlementAsync(string e, string t, int y) => throw new NotImplementedException();
        public Task<int> SeedEntitlementsAsync(string e, int y) => throw new NotImplementedException();
        public Task<double> GetApprovedDaysInRangeAsync(string e, DateTime f, DateTime t) => throw new NotImplementedException();
        public Task<IReadOnlyDictionary<string, double>> GetApprovedUnpaidDaysForOrgAsync(
            DateTime from, DateTime to) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());
        public Task<LeaveOverviewDto> GetOverviewAsync(int y) => throw new NotImplementedException();
        public Task<LeaveSummaryReportResult> GetSummaryReportAsync(string e, int y) => throw new NotImplementedException();
        public Task<LeaveExportResult> ExportSummaryPdfAsync(string e, int y) => throw new NotImplementedException();
        public Task<LeaveApplyResult> ApplyAsync(CreateLeaveApplicationDto d, string e) => throw new NotImplementedException();
        public Task<LeaveApplyResult> EditAsync(string i, CreateLeaveApplicationDto d, string a) => throw new NotImplementedException();
        public Task<LeaveApplyResult> ApplyOnBehalfAsync(string e, CreateLeaveApplicationDto d, string a) => throw new NotImplementedException();
        public Task<LeaveAuditResult> GetAuditTrailAsync(string i) => throw new NotImplementedException();
        public Task<LeaveAttachmentResult> GetAttachmentAsync(string f) => throw new NotImplementedException();
        public Task<LeaveTransitionResult> ApproveAsync(string i, string a) => throw new NotImplementedException();
        public Task<LeaveBulkResult> BulkApproveAsync(IReadOnlyList<string> ids, string a) => throw new NotImplementedException();
        public Task<LeaveTransitionResult> RejectAsync(string i, string a, string? n) => throw new NotImplementedException();
        public Task<LeaveTransitionResult> CancelAsync(string i, string u) => throw new NotImplementedException();
        public Task<IReadOnlyList<OrgApprovalDigestEntryDto>> GetOrgApprovalDigestAsync() =>
            throw new NotImplementedException();
    }
    private sealed class FakeProfileRepository : IEmployeeProfileRepository
    {
        private readonly List<EmployeeProfile> _p;
        public FakeProfileRepository(List<EmployeeProfile> p) => _p = p;

        public Task<EmployeeProfile?> GetByUserAsync(string userId) =>
            Task.FromResult(_p.FirstOrDefault(x => x.UserId == userId));

        public Task<List<EmployeeProfile>> GetAllForCurrentOrgAsync() => Task.FromResult(_p);

        public Task<List<EmployeeProfile>> GetUnarchivedPastLeaversAsync(DateTime before, int max) =>
            throw new NotSupportedException();

        public Task<EmployeeProfile> AddAsync(EmployeeProfile profile)
        {
            _p.Add(profile);
            return Task.FromResult(profile);
        }

        public Task UpdateAsync(EmployeeProfile profile) => Task.CompletedTask;
    }

}

// Records what would have been emailed, so a test can assert the welcome mail
// went out without a mail server.
internal sealed class CapturingEmailSender : IEmailSender
{
    public List<(string To, string Subject, string Html)> Sent { get; } = [];
    public bool Deliver { get; set; } = true;

    public Task<bool> SendAsync(
        string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        Sent.Add((toEmail, subject, htmlBody));
        return Task.FromResult(Deliver);
    }
}

internal sealed class FakeOrgRepositoryForWelcome : IOrganizationRepository
{
    public Task<Organization?> GetByIdAsync(string id) =>
        Task.FromResult<Organization?>(new Organization { Id = id, Name = "Acme Sdn Bhd" });
    public Task<Organization?> GetFirstAsync() => Task.FromResult<Organization?>(null);
    public Task<List<Organization>> GetAllAsync() => Task.FromResult(new List<Organization>());
    public Task AddAsync(Organization organization) => Task.CompletedTask;
    public Task UpdateAsync(Organization organization) => Task.CompletedTask;
    public Task<bool> AnyAsync() => Task.FromResult(true);
}

// Where someone sits in a team, for the guard that stops a supervisor being
// demoted while people still route requests to them. Empty by default: most
// tests here are about roles and emails, not team structure.
internal sealed class FakeApproverPositions : AltomateHR.Api.Modules.Teams.IApproverPositions
{
    public Dictionary<string, List<AltomateHR.Api.Modules.Teams.ApproverPosition>> ByEmployee { get; } = [];

    public void Place(string employeeId, string teamName, int layer)
    {
        if (!ByEmployee.TryGetValue(employeeId, out var list))
        {
            list = [];
            ByEmployee[employeeId] = list;
        }

        list.Add(new AltomateHR.Api.Modules.Teams.ApproverPosition(
            $"team-{teamName}", teamName, layer));
    }

    public Task<IReadOnlyList<AltomateHR.Api.Modules.Teams.ApproverPosition>> ForEmployeeAsync(
        string employeeId) =>
        Task.FromResult<IReadOnlyList<AltomateHR.Api.Modules.Teams.ApproverPosition>>(
            ByEmployee.GetValueOrDefault(employeeId, []));
}
