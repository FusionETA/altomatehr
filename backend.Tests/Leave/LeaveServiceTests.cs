using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Dtos;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Modules.Teams;
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

        var al = (await service.GetBalancesAsync("usr-emp")).Single(b => b.Code == "AL");

        Assert.Equal(14, al.EntitlementDays);
        Assert.Equal(3, al.TakenDays);
        Assert.Equal(2, al.PendingDays);
        Assert.Equal(11, al.RemainingDays);   // pending does NOT reduce the balance
    }

    [Fact]
    public async Task GetBalancesAsync_UsesPolicyEntitlementOverride()
    {
        var service = MakeService(
            types: [MakeType("t-al", "AL", 14)],
            policy: new FakePolicyService(new Dictionary<string, double> { ["t-al"] = 20 }));

        var al = (await service.GetBalancesAsync("usr-emp")).Single(b => b.Code == "AL");

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
        IApprovalRouter? router = null) =>
        new(
            new FakeLeaveApplicationRepository(apps ?? []),
            new FakeLeaveTypeRepository(types ?? []),
            supervision ?? new FakeSupervisionService(),
            policy ?? new FakePolicyService(),
            router ?? new FakeApprovalRouter());

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
        public Task<List<LeaveApplication>> GetAllAsync() => Task.FromResult(_apps.ToList());
        public Task<LeaveApplication?> GetByIdAsync(string id) => Task.FromResult(_apps.FirstOrDefault(a => a.Id == id));
        public Task<List<LeaveApplication>> GetByEmployeeAsync(string employeeId) =>
            Task.FromResult(_apps.Where(a => a.EmployeeId == employeeId).ToList());
        public Task<LeaveApplication> AddAsync(LeaveApplication app) { _apps.Add(app); return Task.FromResult(app); }
        public Task UpdateAsync(LeaveApplication app) => Task.CompletedTask;
    }

    private sealed class FakePolicyService : IPolicyService
    {
        private readonly IReadOnlyDictionary<string, double> _entitlements;
        public FakePolicyService(IReadOnlyDictionary<string, double>? entitlements = null) =>
            _entitlements = entitlements ?? new Dictionary<string, double>();

        public Task<IReadOnlyDictionary<string, double>> GetLeaveEntitlementsAsync(string employeeId) =>
            Task.FromResult(_entitlements);
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
