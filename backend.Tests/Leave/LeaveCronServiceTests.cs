using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Tests.Common;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Leave.Entities;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Tests.Leave;

public class LeaveCronServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private static int Year => 2026;

    [Fact]
    public async Task Accrual_AddsOneTwelfth_ForProRatedTypes()
    {
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 3);
        var svc = MakeService(
            types: [Type("t-al", LeaveAccrualMethod.PRO_RATED)],
            rows: [row]);

        var result = await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(1, result.AccruedCount);
        Assert.Equal(4, row.AccruedDays);       // 3 + 12/12
    }

    [Fact]
    public async Task Accrual_Skips_LumpSumTypes()
    {
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 12);
        var svc = MakeService(
            types: [Type("t-al", LeaveAccrualMethod.LUMP_SUM)],
            rows: [row]);

        var result = await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(0, result.AccruedCount);
        Assert.Equal(12, row.AccruedDays);
    }

    [Fact]
    public async Task Accrual_NeverExceedsEntitlement()
    {
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 11.5);
        var svc = MakeService(types: [Type("t-al", LeaveAccrualMethod.PRO_RATED)], rows: [row]);

        await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(12, row.AccruedDays);      // capped, not 12.5
    }

    [Fact]
    public async Task Accrual_IsIdempotent_OnceCapped()
    {
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 12);
        var svc = MakeService(types: [Type("t-al", LeaveAccrualMethod.PRO_RATED)], rows: [row]);

        var result = await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(0, result.AccruedCount);   // re-running a capped row is a no-op
    }

    [Fact]
    public async Task Accrual_EmployeeOverride_BeatsTheType()
    {
        // Type says LUMP_SUM, but this employee's row is explicitly PRO_RATED.
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 0);
        row.AccrualMethod = LeaveAccrualMethod.PRO_RATED;
        var svc = MakeService(types: [Type("t-al", LeaveAccrualMethod.LUMP_SUM)], rows: [row]);

        var result = await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(1, result.AccruedCount);
    }

    [Fact]
    public async Task Accrual_PolicyOverride_BeatsTheType()
    {
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 0);
        var svc = MakeService(
            types: [Type("t-al", LeaveAccrualMethod.LUMP_SUM)],
            rows: [row],
            memberships: [Membership("e1", policyId: "p1")],
            policyOverrides: [PolicyOverride("p1", "t-al", LeaveAccrualMethod.PRO_RATED)]);

        var result = await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(1, result.AccruedCount);
    }

    [Fact]
    public async Task Sweep_ExpiresCarriedDays_AndRecordsWhatWasForfeited()
    {
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 12);
        row.CarriedDays = 5;
        row.CarriedExpiresAt = Now.AddDays(-1);          // lapsed yesterday
        var svc = MakeService(types: [Type("t-al", LeaveAccrualMethod.LUMP_SUM)], rows: [row]);

        var result = await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(1, result.ExpiredCount);
        Assert.Equal(0, row.CarriedDays);
        Assert.True(row.CarriedExpired);
        Assert.Equal(5, row.CarriedExpiredDays);         // audit trail survives
        Assert.Equal(Now, row.CarriedExpiredAt);
    }

    [Fact]
    public async Task Sweep_LeavesUnexpiredCarryAlone()
    {
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 12);
        row.CarriedDays = 5;
        row.CarriedExpiresAt = Now.AddMonths(3);         // still valid
        var svc = MakeService(types: [Type("t-al", LeaveAccrualMethod.LUMP_SUM)], rows: [row]);

        var result = await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(0, result.ExpiredCount);
        Assert.Equal(5, row.CarriedDays);
    }

    [Fact]
    public async Task Accrual_IgnoresArchivedTypes()
    {
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 0);
        var type = Type("t-al", LeaveAccrualMethod.PRO_RATED);
        type.IsArchived = true;
        var svc = MakeService(types: [type], rows: [row]);

        var result = await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(0, result.AccruedCount);
    }


    // ── year rollover ───────────────────────────────────────────────
    [Fact]
    public async Task Rollover_CreatesOneRowPerEmployeePerActiveType()
    {
        var svc = MakeService(
            types: [Type("t-al", LeaveAccrualMethod.LUMP_SUM)],
            rows: [],
            memberships: [Membership("e1", null), Membership("e2", null)]);

        var result = await svc.RunYearRolloverAsync(2027, Now);

        Assert.Equal(2, result.Created);
        Assert.Equal(0, result.Skipped);
        Assert.All(_repo.Rows, r => Assert.Equal(2027, r.Year));
        Assert.All(_repo.Rows, r => Assert.Equal("org-1", r.OrganizationId));  // stamped explicitly
    }

    [Fact]
    public async Task Rollover_IsIdempotent_SkippingRowsThatAlreadyExist()
    {
        var already = Entitlement("e1", "t-al", entitled: 12, accrued: 12);
        already.Year = 2027;
        var svc = MakeService(
            types: [Type("t-al", LeaveAccrualMethod.LUMP_SUM)],
            rows: [already],
            memberships: [Membership("e1", null)]);

        var result = await svc.RunYearRolloverAsync(2027, Now);

        Assert.Equal(0, result.Created);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public async Task Rollover_ProRated_StartsEmpty_LumpSum_StartsFull()
    {
        var svc = MakeService(
            types: [Type("t-al", LeaveAccrualMethod.PRO_RATED)],
            rows: [],
            memberships: [Membership("e1", null)]);

        await svc.RunYearRolloverAsync(2027, Now);

        Assert.Equal(0, _repo.Rows.Single().AccruedDays);       // fills monthly
        Assert.Equal(12, _repo.Rows.Single().EntitledDays);
    }

    [Fact]
    public async Task Rollover_CarriesUnusedDays_WhenTheTypeAllowsIt()
    {
        var prev = Entitlement("e1", "t-al", entitled: 12, accrued: 12);
        var svc = MakeService(
            types: [CarryType("t-al", maxCarry: null, expiryMonth: 4)],
            rows: [prev],
            memberships: [Membership("e1", null)],
            apps: [Approved("e1", "t-al", 5, Year)]);       // used 5 of 12

        await svc.RunYearRolloverAsync(Year + 1, Now);

        var created = _repo.Rows.Single(r => r.Year == Year + 1);
        Assert.Equal(7, created.CarriedDays);                       // 12 - 5
        Assert.Equal(new DateTime(Year + 1, 4, 1, 0, 0, 0, DateTimeKind.Utc), created.CarriedExpiresAt);
    }

    [Fact]
    public async Task Rollover_CapsCarryForward_AtTheTypeCeiling()
    {
        var prev = Entitlement("e1", "t-al", entitled: 12, accrued: 12);
        var svc = MakeService(
            types: [CarryType("t-al", maxCarry: 3, expiryMonth: 4)],
            rows: [prev],
            memberships: [Membership("e1", null)],
            apps: []);                                       // used nothing → 12 available

        await svc.RunYearRolloverAsync(Year + 1, Now);

        Assert.Equal(3, _repo.Rows.Single(r => r.Year == Year + 1).CarriedDays);   // capped
    }

    [Fact]
    public async Task Rollover_CarriesNothing_WhenTheTypeDoesNotAllowIt()
    {
        var prev = Entitlement("e1", "t-al", entitled: 12, accrued: 12);
        var svc = MakeService(
            types: [Type("t-al", LeaveAccrualMethod.LUMP_SUM)],   // CarryForward = false
            rows: [prev],
            memberships: [Membership("e1", null)]);

        await svc.RunYearRolloverAsync(Year + 1, Now);

        Assert.Equal(0, _repo.Rows.Single(r => r.Year == Year + 1).CarriedDays);
    }

    [Fact]
    public async Task Sweep_ForfeitsOnlyTheUNUSEDCarriedPortion()
    {
        // 12 entitled + 5 carried, 14 used. The current-year bucket (12) is spent
        // first, so 2 of the carried days were used — only 3 should be forfeited.
        var row = Entitlement("e1", "t-al", entitled: 12, accrued: 12);
        row.CarriedDays = 5;
        row.CarriedExpiresAt = Now.AddDays(-1);
        var svc = MakeService(
            types: [Type("t-al", LeaveAccrualMethod.LUMP_SUM)],
            rows: [row],
            apps: [Approved("e1", "t-al", 14, Year)]);

        var result = await svc.RunMonthlyAccrualAsync(Now);

        Assert.Equal(1, result.ExpiredCount);
        Assert.Equal(3, row.CarriedExpiredDays);    // forfeited
        Assert.Equal(2, row.CarriedDays);           // the portion actually spent
    }

    // ── helpers ─────────────────────────────────────────────────────
    private static FakeEntitlementRepo _repo = null!;

    private static LeaveCronService MakeService(
        IEnumerable<LeaveType> types,
        IEnumerable<LeaveEntitlement> rows,
        IEnumerable<OrganizationMembership>? memberships = null,
        IEnumerable<PolicyLeaveEntitlement>? policyOverrides = null,
        IEnumerable<LeaveApplication>? apps = null)
    {
        _repo = new FakeEntitlementRepo(rows);
        return new LeaveCronService(
            _repo,
            new FakeTypeRepo(types),
            new FakeAppRepo(apps ?? []),
            new FakePolicyService(policyOverrides ?? []),
            TestDirectory.Over(new FakeMembershipRepo(memberships ?? [])));
    }

    private static LeaveApplication Approved(string emp, string type, double days, int year) => new()
    {
        Id = Guid.NewGuid().ToString(), EmployeeId = emp, LeaveTypeId = type,
        TotalDays = days, Status = LeaveStatus.APPROVED,
        StartDate = new DateTime(year, 3, 1), EndDate = new DateTime(year, 3, 1),
    };

    private sealed class FakeAppRepo(IEnumerable<LeaveApplication> apps) : ILeaveApplicationRepository
    {
        public Task<List<LeaveApplication>> GetAllAsync() => Task.FromResult(apps.ToList());
        public Task<LeaveApplication?> GetByIdAsync(string id) => throw new NotImplementedException();
        public Task<LeaveApplication?> GetByXeroFileIdAsync(string fileId) =>
            Task.FromResult(apps.FirstOrDefault(a => a.XeroFileId == fileId));
        public Task<List<LeaveApplication>> GetByEmployeeAsync(string e) =>
            Task.FromResult(apps.Where(a => a.EmployeeId == e).ToList());
        public Task<LeaveApplication> AddAsync(LeaveApplication a) => throw new NotImplementedException();
        public Task UpdateAsync(LeaveApplication a) => throw new NotImplementedException();
    }

    private static LeaveType Type(string id, LeaveAccrualMethod method) => new()
    { Id = id, OrganizationId = "org-1", Code = "AL", Name = "Annual", AccrualMethod = method, DefaultDays = 12 };

    private static LeaveEntitlement Entitlement(string emp, string type, double entitled, double accrued) => new()
    { Id = Guid.NewGuid().ToString(), OrganizationId = "org-1", EmployeeId = emp,
      LeaveTypeId = type, Year = Year, EntitledDays = entitled, AccruedDays = accrued };

    private static OrganizationMembership Membership(string userId, string? policyId) => new()
    { UserId = userId, OrganizationId = "org-1", Role = "Employee", PolicyId = policyId };

    private static PolicyLeaveEntitlement PolicyOverride(string policyId, string typeId, LeaveAccrualMethod m) => new()
    { PolicyId = policyId, LeaveTypeId = typeId, AccrualMethod = m };

    private static LeaveType CarryType(string id, double? maxCarry, int? expiryMonth) => new()
    { Id = id, OrganizationId = "org-1", Code = "AL", Name = "Annual", DefaultDays = 12,
      AccrualMethod = LeaveAccrualMethod.LUMP_SUM, CarryForward = true,
      MaxCarryForwardDays = maxCarry, CarryExpiryMonth = expiryMonth };

    private sealed class FakeEntitlementRepo(IEnumerable<LeaveEntitlement> rows) : ILeaveEntitlementRepository
    {
        private readonly List<LeaveEntitlement> _rows = rows.ToList();
        public IReadOnlyList<LeaveEntitlement> Rows => _rows;
        public Task<List<LeaveEntitlement>> GetByYearAsync(int year) =>
            Task.FromResult(_rows.Where(r => r.Year == year).ToList());
        public Task<List<LeaveEntitlement>> GetForEmployeeYearAsync(string employeeId, int year) =>
            Task.FromResult(_rows.Where(r => r.EmployeeId == employeeId && r.Year == year).ToList());
        public Task<List<LeaveEntitlement>> GetCarryExpiringAsync(DateTime asOf) =>
            Task.FromResult(_rows.Where(r => !r.CarriedExpired && r.CarriedDays > 0
                && r.CarriedExpiresAt != null && r.CarriedExpiresAt <= asOf).ToList());
        public Task AddAsync(LeaveEntitlement e) { _rows.Add(e); return Task.CompletedTask; }
        public Task SaveAsync() => Task.CompletedTask;
    }

    private sealed class FakeTypeRepo(IEnumerable<LeaveType> types) : ILeaveTypeRepository
    {
        private readonly List<LeaveType> _types = types.ToList();
        public Task<List<LeaveType>> GetAllAsync() => Task.FromResult(_types);
        public Task<LeaveType?> GetByIdAsync(string id) => Task.FromResult(_types.FirstOrDefault(t => t.Id == id));
        public Task<LeaveType?> GetByCodeAsync(string code) => Task.FromResult(_types.FirstOrDefault(t => t.Code == code));
        public Task<LeaveType> AddAsync(LeaveType t) { _types.Add(t); return Task.FromResult(t); }
        public Task UpdateAsync(LeaveType t) => Task.CompletedTask;
    }

    // LeaveCronService asks the Policies SERVICE now, not its repository, so the
    // fake moved up a layer. Only the bulk entitlement read is exercised here.
    private sealed class FakePolicyService(IEnumerable<PolicyLeaveEntitlement> rows)
        : IPolicyService
    {
        public Task<IReadOnlyList<PolicyLeaveEntitlement>> GetAllPolicyEntitlementsAsync() =>
            Task.FromResult<IReadOnlyList<PolicyLeaveEntitlement>>(rows.ToList());

        public Task<IReadOnlyList<EmployeePolicy>> GetAllAcrossOrgsAsync() =>
            Task.FromResult<IReadOnlyList<EmployeePolicy>>([]);
        Task<IEnumerable<PolicyDto>> IPolicyService.GetAllAsync() =>
            Task.FromResult<IEnumerable<PolicyDto>>([]);
        public Task<PolicySaveResult> CreateAsync(SavePolicyDto dto) => throw new NotSupportedException();
        public Task<PolicySaveResult> UpdateAsync(string id, SavePolicyDto dto) => throw new NotSupportedException();
        public Task<PolicyDto?> SetArchivedAsync(string id, bool archived) => throw new NotSupportedException();
        public Task<PolicyDto?> SetDefaultAsync(string id) => throw new NotSupportedException();
        public Task<EmployeePolicy?> GetEffectivePolicyAsync(string employeeId) =>
            Task.FromResult<EmployeePolicy?>(null);
        public Task<bool> RequiresGeofenceAsync(string employeeId) => Task.FromResult(false);
        public Task<IReadOnlyDictionary<string, double>> GetLeaveEntitlementsAsync(string employeeId) =>
            Task.FromResult<IReadOnlyDictionary<string, double>>(new Dictionary<string, double>());
        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>
            GetLeaveEntitlementsForEmployeesAsync(IEnumerable<string> employeeIds) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyDictionary<string, double>>>(
                new Dictionary<string, IReadOnlyDictionary<string, double>>());
    }

    private sealed class FakeMembershipRepo(IEnumerable<OrganizationMembership> rows)
        : IOrganizationMembershipRepository
    {
        public Task<List<OrganizationMembership>> GetForCurrentOrgAsync() => Task.FromResult(rows.ToList());
        public Task<List<OrganizationMembership>> GetByUserAsync(string userId) => throw new NotImplementedException();
        public Task<OrganizationMembership?> GetAsync(string o, string u) => throw new NotImplementedException();
        public Task<OrganizationMembership?> GetForUserInCurrentOrgAsync(string u) => throw new NotImplementedException();
        public Task<List<OrganizationMembership>> GetBySupervisorAsync(string s) => throw new NotImplementedException();
        public Task<int> CountByShiftIdAsync(string shiftId) => Task.FromResult(0);
        public Task AddAsync(OrganizationMembership m) => throw new NotImplementedException();
        public Task UpdateAsync(OrganizationMembership m) => throw new NotImplementedException();
    }
}
