using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Teams;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Tests.Claims;   // reuse FakeSupervisionService + FakeApprovalRouter

namespace AltomateHR.Api.Tests.Leave;

public class LeaveServiceTests
{
    private static readonly int Year = DateTime.UtcNow.Year;

    [Fact]
    public async Task ApplyAsync_ComputesInclusiveDaySpanAndStartsPending()
    {
        var service = MakeService(types: [MakeType("t-al", "AL", 14)]);

        var result = await service.ApplyAsync(
            new CreateLeaveApplicationDto
            {
                LeaveTypeId = "t-al",
                StartDate = new DateTime(Year, 9, 1),
                EndDate = new DateTime(Year, 9, 3),
            },
            "usr-emp");

        Assert.True(result.Ok);
        Assert.Equal(3, result.Application!.TotalDays);   // 1st..3rd inclusive
        Assert.Equal(LeaveStatus.PENDING, result.Application.Status);
    }

    [Fact]
    public async Task ApplyAsync_RejectsWhenEndBeforeStart()
    {
        var service = MakeService(types: [MakeType("t-al", "AL", 14)]);

        var result = await service.ApplyAsync(
            new CreateLeaveApplicationDto
            {
                LeaveTypeId = "t-al",
                StartDate = new DateTime(Year, 9, 5),
                EndDate = new DateTime(Year, 9, 3),
            },
            "usr-emp");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ApplyAsync_RejectsUnknownOrArchivedType()
    {
        var service = MakeService(types: [MakeType("t-al", "AL", 14, archived: true)]);

        var unknown = await service.ApplyAsync(NewDto("ghost"), "usr-emp");
        var archived = await service.ApplyAsync(NewDto("t-al"), "usr-emp");

        Assert.False(unknown.Ok);
        Assert.False(archived.Ok);
    }

    [Fact]
    public async Task GetBalancesAsync_SubtractsApprovedButNotPending()
    {
        var apps = new[]
        {
            MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.APPROVED),
            MakeApp("a2", "usr-emp", "t-al", 2, LeaveStatus.PENDING),
        };
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], apps: apps);

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single(b => b.Code == "AL");

        Assert.Equal(14, al.EntitlementDays);
        Assert.Equal(3, al.TakenDays);
        Assert.Equal(2, al.PendingDays);
        Assert.Equal(11, al.RemainingDays);   // pending does NOT reduce the balance
    }

    [Fact]
    public async Task GetBalancesForEmployeeAsync_Allows_AnEmployeeToReadTheirOwn()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            memberships: new FakeMembershipRepository("usr-emp"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"));

        var result = await service.GetBalancesForEmployeeAsync("usr-emp", Year);

        Assert.True(result.Found);
        Assert.True(result.Allowed);
    }

    [Fact]
    public async Task GetBalancesForEmployeeAsync_Refuses_AnEmployeeReadingSomeoneElse()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            memberships: new FakeMembershipRepository("usr-emp", "usr-two"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"));

        var result = await service.GetBalancesForEmployeeAsync("usr-two", Year);

        Assert.True(result.Found);        // they exist in this org...
        Assert.False(result.Allowed);     // ...but it's not the caller's to read (403)
        Assert.Empty(result.Balances);
    }

    [Fact]
    public async Task GetBalancesAsync_PrefersTheStoredEntitlementRow_OverTheTypeDefault()
    {
        // Type says 14, but the stored row (what the rollover wrote) says 20.
        var row = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 20, AccruedDays = 20,
        };
        var service = MakeService(types: [MakeType("t-al", "AL", 14)], entitlementRows: [row]);

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single();

        Assert.True(al.IsOpened);
        Assert.Equal(20, al.EntitlementDays);
        Assert.Equal(20, al.RemainingDays);
    }

    [Fact]
    public async Task GetBalancesAsync_ProRated_ShowsOnlyWhatHasAccrued()
    {
        var row = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 12, AccruedDays = 5,
            AccrualMethod = LeaveAccrualMethod.PRO_RATED,
        };
        var service = MakeService(types: [MakeType("t-al", "AL", 12)], entitlementRows: [row]);

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single();

        Assert.Equal(12, al.EntitlementDays);   // the year's full entitlement...
        Assert.Equal(5, al.AccruedDays);        // ...but only 5 earned so far
        Assert.Equal(5, al.RemainingDays);      // so 5 is what she can apply for
    }

    [Fact]
    public async Task GetBalancesAsync_AddsUnexpiredCarry_ButIgnoresExpiredCarry()
    {
        var live = new LeaveEntitlement
        {
            EmployeeId = "usr-emp", LeaveTypeId = "t-al", Year = Year,
            EntitledDays = 12, AccruedDays = 12, CarriedDays = 3,
        };
        var service = MakeService(types: [MakeType("t-al", "AL", 12)], entitlementRows: [live]);
        Assert.Equal(15, (await service.GetBalancesAsync("usr-emp", Year)).Single().RemainingDays);

        live.CarriedExpired = true;             // the sweep has run
        Assert.Equal(12, (await service.GetBalancesAsync("usr-emp", Year)).Single().RemainingDays);
    }

    [Fact]
    public async Task GetBalancesAsync_FallsBackToTheTypeDefault_WhenTheYearIsNotOpened()
    {
        var service = MakeService(types: [MakeType("t-al", "AL", 14)]);   // no entitlement rows

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single();

        Assert.False(al.IsOpened);              // projection, not stored state
        Assert.Equal(14, al.EntitlementDays);
        Assert.Equal(14, al.RemainingDays);
    }

    [Fact]
    public async Task GetAttachment_ReturnsTheFile_ForTheApplicationOwner()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 2, LeaveStatus.APPROVED);
        app.XeroFileId = "file-1";
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app],
            memberships: new FakeMembershipRepository("usr-emp"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"),
            xero: new FakeXeroService(new XeroFileContent([1, 2, 3], "image/png", "mc.png")));

        var result = await service.GetAttachmentAsync("file-1");

        Assert.True(result.Found);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal("mc.png", result.FileName);
    }

    [Fact]
    public async Task GetAttachment_Is404_ForSomeoneElsesAttachment()
    {
        // Belongs to usr-two; usr-emp is a plain employee, so it must look
        // identical to "no such file" — not a 403 that confirms it exists.
        var app = MakeApp("a1", "usr-two", "t-al", 2, LeaveStatus.APPROVED);
        app.XeroFileId = "file-1";
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)], apps: [app],
            memberships: new FakeMembershipRepository("usr-emp", "usr-two"),
            currentUser: new FakeCurrentUser("usr-emp", "Employee"),
            xero: new FakeXeroService(new XeroFileContent([1], "image/png", "mc.png")));

        var result = await service.GetAttachmentAsync("file-1");

        Assert.False(result.Found);
        Assert.Empty(result.Content);
    }

    [Fact]
    public async Task GetAttachment_Is404_WhenNoApplicationOwnsTheFile()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            xero: new FakeXeroService(new XeroFileContent([1], "image/png", "x.png")));

        // Xero would happily return bytes — but no leave application claims this
        // id, so it must never be served.
        Assert.False((await service.GetAttachmentAsync("file-unknown")).Found);
    }

    [Fact]
    public async Task GetOrgBalancesAsync_ReturnsOneRowPerMember_WithTheirOwnBalances()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.APPROVED)],
            memberships: new FakeMembershipRepository("usr-emp", "usr-two"));

        var rows = (await service.GetOrgBalancesAsync(Year)).ToList();

        Assert.Equal(2, rows.Count);
        // usr-emp took 3 days; usr-two took none — balances must not bleed across employees.
        Assert.Equal(11, rows.Single(r => r.UserId == "usr-emp").Balances.Single().RemainingDays);
        Assert.Equal(14, rows.Single(r => r.UserId == "usr-two").Balances.Single().RemainingDays);
    }

    [Fact]
    public async Task GetBalancesForEmployeeAsync_ReturnsBalances_WhenEmployeeIsInCurrentOrg()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.APPROVED)],
            memberships: new FakeMembershipRepository("usr-emp"));

        var result = await service.GetBalancesForEmployeeAsync("usr-emp", Year);

        Assert.True(result.Found);
        Assert.True(result.Allowed);
        Assert.Equal(Year, result.Year);
        Assert.Equal(11, result.Balances.Single(b => b.Code == "AL").RemainingDays);
    }

    [Fact]
    public async Task GetBalancesForEmployeeAsync_NotFound_WhenEmployeeIsInAnotherOrg()
    {
        // "usr-other" is not a member of the caller's org — must not leak data.
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            memberships: new FakeMembershipRepository("usr-emp"));

        var result = await service.GetBalancesForEmployeeAsync("usr-other", Year);

        Assert.False(result.Found);
        Assert.Empty(result.Balances);
    }

    [Fact]
    public async Task GetBalancesAsync_UsesPolicyEntitlementOverride()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            policy: new FakePolicyService(new Dictionary<string, double> { ["t-al"] = 20 }));

        var al = (await service.GetBalancesAsync("usr-emp", Year)).Single(b => b.Code == "AL");

        Assert.Equal(20, al.EntitlementDays);   // policy override wins over the type default (14)
        Assert.Equal(20, al.RemainingDays);
    }

    [Fact]
    public async Task ApproveAsync_AllowsCurrentStepApprover()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.PENDING)],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var result = await service.ApproveAsync("a1", "usr-super");

        Assert.True(result.Transitioned);
        Assert.Equal(LeaveStatus.APPROVED, result.Application!.Status);
    }

    [Fact]
    public async Task ApproveAsync_HidesApplicationFromNonCurrentApprover()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [MakeApp("a1", "usr-emp", "t-al", 3, LeaveStatus.PENDING)],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var result = await service.ApproveAsync("a1", "usr-other-super");

        Assert.False(result.Found);
    }

    [Fact]
    public async Task ApproveAsync_AdvancesThroughAMultiStepChain()
    {
        var app = MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.PENDING);
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps: [app],
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"], ["usr-mgr"]] }));

        var first = await service.ApproveAsync("a1", "usr-super");   // step 0 → advance
        Assert.True(first.Transitioned);
        Assert.Equal(LeaveStatus.PENDING, first.Application!.Status);
        Assert.Equal(1, app.CurrentStep);

        // The step-0 approver can no longer act.
        Assert.False((await service.ApproveAsync("a1", "usr-super")).Found);

        var second = await service.ApproveAsync("a1", "usr-mgr");    // step 1 → final
        Assert.True(second.Transitioned);
        Assert.Equal(LeaveStatus.APPROVED, second.Application!.Status);
    }

    [Fact]
    public async Task GetTeamAsync_ReturnsOnlyCurrentApproverApplicationsWithEmail()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            apps:
            [
                MakeApp("mine", "usr-emp", "t-al", 1, LeaveStatus.PENDING),
                MakeApp("other", "usr-other", "t-al", 1, LeaveStatus.PENDING),
            ],
            supervision: new FakeSupervisionService(emails: new() { ["usr-emp"] = "employee@altomate.com" }),
            router: new FakeApprovalRouter(new() { ["usr-emp"] = [["usr-super"]] }));

        var team = (await service.GetTeamAsync("usr-super")).ToList();

        Assert.Single(team);
        Assert.Equal("mine", team[0].Id);
        Assert.Equal("employee@altomate.com", team[0].EmployeeEmail);
    }

    [Fact]
    public async Task CancelAsync_OwnerCancelsPending_ButOthersCannot()
    {
        var service = MakeService(apps: [MakeApp("a1", "usr-emp", "t-al", 1, LeaveStatus.PENDING)]);

        var byOther = await service.CancelAsync("a1", "usr-other");
        Assert.False(byOther.Found);

        var byOwner = await service.CancelAsync("a1", "usr-emp");
        Assert.True(byOwner.Transitioned);
        Assert.Equal(LeaveStatus.CANCELLED, byOwner.Application!.Status);
    }

    // --- helpers ---

    private static CreateLeaveApplicationDto NewDto(string typeId) => new()
    {
        LeaveTypeId = typeId,
        StartDate = new DateTime(Year, 9, 1),
        EndDate = new DateTime(Year, 9, 2),
    };

    private static LeaveService MakeService(
        IEnumerable<LeaveType>? types = null,
        IEnumerable<LeaveApplication>? apps = null,
        ISupervisionService? supervision = null,
        IPolicyService? policy = null,
        IApprovalRouter? router = null,
        IOrganizationMembershipRepository? memberships = null,
        ICurrentUser? currentUser = null,
        IEnumerable<LeaveEntitlement>? entitlementRows = null,
        IXeroService? xero = null) =>
        new(
            new FakeLeaveApplicationRepository(apps ?? []),
            new FakeLeaveTypeRepository(types ?? []),
            supervision ?? new FakeSupervisionService(),
            policy ?? new FakePolicyService(),
            router ?? new FakeApprovalRouter(),
            memberships ?? new FakeMembershipRepository("usr-emp"),
            currentUser ?? new FakeCurrentUser("usr-admin", "Admin"),
            new FakeEntitlementRepo(entitlementRows ?? []),
            xero ?? new FakeXeroService());

    private static LeaveType MakeType(string id, string code, double days, bool paid = true, bool archived = false) => new()
    {
        Id = id,
        Code = code,
        Name = code,
        Paid = paid,
        DefaultDays = days,
        IsArchived = archived,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static LeaveApplication MakeApp(string id, string emp, string typeId, double days, LeaveStatus status) => new()
    {
        Id = id,
        EmployeeId = emp,
        LeaveTypeId = typeId,
        StartDate = new DateTime(Year, 9, 1),
        EndDate = new DateTime(Year, 9, 1).AddDays(days - 1),
        TotalDays = days,
        Status = status,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private sealed class FakeLeaveTypeRepository : ILeaveTypeRepository
    {
        private readonly List<LeaveType> _types;
        public FakeLeaveTypeRepository(IEnumerable<LeaveType> types) => _types = types.ToList();
        public Task<List<LeaveType>> GetAllAsync() => Task.FromResult(_types.ToList());
        public Task<LeaveType?> GetByIdAsync(string id) => Task.FromResult(_types.FirstOrDefault(t => t.Id == id));
        public Task<LeaveType?> GetByCodeAsync(string code) => Task.FromResult(_types.FirstOrDefault(t => t.Code == code));
        public Task<LeaveType> AddAsync(LeaveType type) { _types.Add(type); return Task.FromResult(type); }
        public Task UpdateAsync(LeaveType type) => Task.CompletedTask;
    }

    private sealed class FakeLeaveApplicationRepository : ILeaveApplicationRepository
    {
        private readonly List<LeaveApplication> _apps;
        public FakeLeaveApplicationRepository(IEnumerable<LeaveApplication> apps) => _apps = apps.ToList();
        public Task<LeaveApplication?> GetByXeroFileIdAsync(string fileId) =>
            Task.FromResult(_apps.FirstOrDefault(a => a.XeroFileId == fileId));
        public Task<List<LeaveApplication>> GetAllAsync() => Task.FromResult(_apps.ToList());
        public Task<LeaveApplication?> GetByIdAsync(string id) => Task.FromResult(_apps.FirstOrDefault(a => a.Id == id));
        public Task<List<LeaveApplication>> GetByEmployeeAsync(string employeeId) =>
            Task.FromResult(_apps.Where(a => a.EmployeeId == employeeId).ToList());
        public Task<LeaveApplication> AddAsync(LeaveApplication app) { _apps.Add(app); return Task.FromResult(app); }
        public Task UpdateAsync(LeaveApplication app) => Task.CompletedTask;
    }

    // Only GetForUserInCurrentOrgAsync matters here: the ids passed to the ctor
    // are the "members of the current org"; anything else resolves to null (→ 404).
    private sealed class FakeMembershipRepository(params string[] memberIds)
        : IOrganizationMembershipRepository
    {
        public Task<OrganizationMembership?> GetForUserInCurrentOrgAsync(string userId) =>
            Task.FromResult(memberIds.Contains(userId)
                ? new OrganizationMembership { UserId = userId, Role = "Employee" }
                : null);

        public Task<List<OrganizationMembership>> GetByUserAsync(string userId) => throw new NotImplementedException();
        public Task<OrganizationMembership?> GetAsync(string organizationId, string userId) => throw new NotImplementedException();
        public Task<List<OrganizationMembership>> GetForCurrentOrgAsync() =>
            Task.FromResult(memberIds
                .Select(id => new OrganizationMembership { UserId = id, Role = "Employee" })
                .ToList());
        public Task<List<OrganizationMembership>> GetBySupervisorAsync(string supervisorId) => throw new NotImplementedException();
        public Task AddAsync(OrganizationMembership membership) => throw new NotImplementedException();
        public Task UpdateAsync(OrganizationMembership membership) => throw new NotImplementedException();
    }

    private sealed class FakeEntitlementRepo(IEnumerable<LeaveEntitlement> rows) : ILeaveEntitlementRepository
    {
        public Task<List<LeaveEntitlement>> GetByYearAsync(int year) =>
            Task.FromResult(rows.Where(r => r.Year == year).ToList());
        public Task<List<LeaveEntitlement>> GetForEmployeeYearAsync(string employeeId, int year) =>
            Task.FromResult(rows.Where(r => r.EmployeeId == employeeId && r.Year == year).ToList());
        public Task<List<LeaveEntitlement>> GetCarryExpiringAsync(DateTime asOf) =>
            Task.FromResult(new List<LeaveEntitlement>());
        public Task AddAsync(LeaveEntitlement e) => Task.CompletedTask;
        public Task SaveAsync() => Task.CompletedTask;
    }

    // Leave only ever asks Xero for file content; the sync/connect surface is
    // irrelevant here, so it throws if anything else is touched.
    private sealed class FakeXeroService(XeroFileContent? file = null) : IXeroService
    {
        public Task<XeroFileContent?> GetFileContentAsync(string fileId) => Task.FromResult(file);
        public Task<XeroConnectUrlDto> CreateConnectUrlAsync(string? r) => throw new NotImplementedException();
        public Task<string> CompleteCallbackAsync(string c, string s) => throw new NotImplementedException();
        public Task<XeroStatusDto> GetStatusAsync() => throw new NotImplementedException();
        public Task DisconnectAsync() => throw new NotImplementedException();
        public Task<XeroSyncAccountsResultDto> SyncAccountsAsync() => throw new NotImplementedException();
        public Task<XeroSyncProjectsResultDto> SyncProjectsAsync() => throw new NotImplementedException();
    }

    private sealed class FakeCurrentUser(string? userId, string? role) : ICurrentUser
    {
        public string? UserId => userId;
        public string? Role => role;
        public string? OrganizationId => "org-1";
        public bool IsAdmin => role is "Admin" or "Owner";
        public bool IsAuthenticated => userId is not null;
    }

    private sealed class FakePolicyService : IPolicyService
    {
        private readonly IReadOnlyDictionary<string, double> _entitlements;
        public FakePolicyService(IReadOnlyDictionary<string, double>? entitlements = null) =>
            _entitlements = entitlements ?? new Dictionary<string, double>();

        public Task<IReadOnlyDictionary<string, double>> GetLeaveEntitlementsAsync(string employeeId) =>
            Task.FromResult(_entitlements);
        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
            GetLeaveEntitlementsForEmployeesAsync(IEnumerable<string> employeeIds) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>(
                employeeIds.Distinct().ToDictionary(id => id, _ => _entitlements));
        public Task<bool> RequiresGeofenceAsync(string employeeId) => Task.FromResult(true);
        public Task<EmployeePolicy?> GetEffectivePolicyAsync(string employeeId) =>
            Task.FromResult<EmployeePolicy?>(null);
        public Task<IEnumerable<PolicyDto>> GetAllAsync() => throw new NotImplementedException();
        public Task<PolicySaveResult> CreateAsync(SavePolicyDto dto) => throw new NotImplementedException();
        public Task<PolicySaveResult> UpdateAsync(string id, SavePolicyDto dto) => throw new NotImplementedException();
        public Task<PolicyDto?> SetArchivedAsync(string id, bool archived) => throw new NotImplementedException();
        public Task<PolicyDto?> SetDefaultAsync(string id) => throw new NotImplementedException();
    }
}
