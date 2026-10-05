using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Dashboard;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Projects.Dtos;
using AltomateHR.Api.Tests.Claims;
using AltomateHR.Api.Tests.Support;
using static AltomateHR.Api.Tests.Claims.ClaimsTestFactory;

namespace AltomateHR.Api.Tests.Dashboard;

// The two claims cards the admin dashboard opens on: what is late (and with
// whom), and whose approvals a higher layer went on to reject.
public class AdminOverviewServiceTests
{
    private static readonly EmployeeIdentity Ahmad =
        new("usr-ahmad", "ahmad@x.com", "Ahmad Ali", "Employee");

    private static readonly EmployeeIdentity Siti =
        new("usr-siti", "siti@x.com", "Siti Nur", "Employee");

    private static readonly EmployeeIdentity Aisha =
        new("usr-aisha", "aisha@x.com", "Aisha Rahman", "Supervisor");

    // Ahmad reports to Aisha, then to a second layer. Siti reports to Aisha only.
    private static FakeApprovalRouter Chain() =>
        new(new()
        {
            ["usr-ahmad"] = [["usr-aisha"], ["usr-finance"]],
            ["usr-siti"] = [["usr-aisha"]],
        });

    private static Claim Aged(
        string id,
        string employeeId,
        int daysAgo,
        ClaimStatus status = ClaimStatus.PENDING,
        int currentStep = 0)
    {
        var claim = NewClaim(id, employeeId, status);
        claim.SubmittedAt = DateTime.UtcNow.AddDays(-daysAgo);
        claim.UpdatedAt = DateTime.UtcNow.AddDays(-daysAgo);
        claim.CurrentStep = currentStep;
        return claim;
    }

    private static AdminOverviewService Create(
        IEnumerable<Claim> claims,
        FakeApprovalRouter? router = null,
        params string[] modules) => Create(claims, [], router, modules);

    private static AdminOverviewService Create(
        IEnumerable<Claim> claims,
        IReadOnlyList<WorkPermitDto> permits,
        FakeApprovalRouter? router = null,
        params string[] modules)
    {
        var directory = new FakeEmployeeDirectory(Ahmad, Siti, Aisha);
        var enabled = modules.Length > 0 ? modules : [OrgModules.Claims];

        return new AdminOverviewService(
            CreateService(claims, employees: directory),
            new FakeProjectServiceForExport(new ProjectDto { Id = "proj-1", Name = "HQ" }),
            new FakeModuleAccessService(enabled),
            router ?? Chain(),
            directory,
            new FakeWorkPermits(permits));
    }

    // ---- Work permits ----

    private static readonly DateTime Today = new(2026, 10, 5);

    private static WorkPermitDto Permit(
        string userId, int daysFromToday, string nationality = "Indonesian",
        bool pr = false, bool archived = false, DateTime? left = null) => new()
        {
            UserId = userId,
            Nationality = nationality,
            HasPr = pr,
            IsArchived = archived,
            LeaveDate = left,
            WorkPermitNumber = "PLKS-" + userId,
            WorkPermitExpiry = Today.AddDays(daysFromToday),
        };

    // Lapsed and lapsing-within-60-days permits, soonest first; later ones wait.
    [Fact]
    public async Task WorkPermitAlerts_ListExpiredAndSoonToExpire_SoonestFirst()
    {
        var service = Create([], [Permit("usr-siti", 30), Permit("usr-ahmad", -3), Permit("usr-aisha", 61)]);

        var alerts = await service.WorkPermitAlertsAsync(Today);

        Assert.Equal(["Ahmad Ali", "Siti Nur"], alerts.Select(a => a.EmployeeName));
        Assert.Equal([-3, 30], alerts.Select(a => a.DaysLeft));
        Assert.Equal("PLKS-usr-ahmad", alerts[0].WorkPermitNumber);
    }

    // A citizen or PR has no permit to renew; nobody archived or gone needs one.
    [Fact]
    public async Task WorkPermitAlerts_LeaveOutCitizensPrsAndPeopleWhoLeft()
    {
        var service = Create([],
        [
            Permit("usr-1", 5, nationality: "Malaysian"),
            Permit("usr-2", 5, pr: true),
            Permit("usr-3", 5, archived: true),
            Permit("usr-4", 5, left: Today.AddDays(-1)),
            Permit("usr-5", 5, nationality: ""),
            Permit("usr-siti", 60),
        ]);

        var alerts = await service.WorkPermitAlertsAsync(Today);

        Assert.Equal(["usr-siti"], alerts.Select(a => a.EmployeeId));
    }

    // ---- Stale pending claims ----

    [Fact]
    public async Task StalePendingClaims_OnlyIncludesClaimsPendingLongerThanSevenDays()
    {
        var overview = await Create([
            Aged("fresh", "usr-ahmad", 2),
            Aged("late", "usr-ahmad", 12),
        ]).GetAsync();

        var stale = Assert.Single(overview.StalePendingClaims);
        Assert.Equal("late", stale.Id);
        Assert.Equal(12, stale.DaysPending);
    }

    [Fact]
    public async Task StalePendingClaims_AreOldestFirst()
    {
        var overview = await Create([
            Aged("recent", "usr-ahmad", 9),
            Aged("ancient", "usr-ahmad", 40),
            Aged("middle", "usr-ahmad", 20),
        ]).GetAsync();

        Assert.Equal(
            ["ancient", "middle", "recent"],
            overview.StalePendingClaims.Select(c => c.Id));
    }

    [Fact]
    public async Task StalePendingClaims_IgnoreClaimsThatAreNoLongerPending()
    {
        var overview = await Create([
            Aged("approved", "usr-ahmad", 30, ClaimStatus.APPROVED),
            Aged("rejected", "usr-ahmad", 30, ClaimStatus.REJECTED),
        ]).GetAsync();

        Assert.Empty(overview.StalePendingClaims);
    }

    [Fact]
    public async Task StalePendingClaims_NameTheApproverOfTheStepTheClaimStalledAt()
    {
        // Step 1 — Aisha already signed off; it is finance that is sitting on it.
        var overview = await Create([Aged("late", "usr-ahmad", 15, currentStep: 1)]).GetAsync();

        var stale = Assert.Single(overview.StalePendingClaims);
        Assert.Equal(["usr-finance"], stale.CurrentApprovers);
        Assert.Equal("Ahmad Ali", stale.EmployeeName);
    }

    [Fact]
    public async Task StalePendingClaims_HaveNoApproversWhenTheClaimIsUnrouted()
    {
        // No chain for this employee at all — nobody can approve it, which the
        // dashboard surfaces separately from a merely slow approver.
        var overview = await Create([Aged("late", "usr-nobody", 15)]).GetAsync();

        var stale = Assert.Single(overview.StalePendingClaims);
        Assert.Empty(stale.CurrentApprovers);
    }

    // ---- Overturned approvers ----

    [Fact]
    public async Task OverturnedSupervisors_CountRejectionsThatGotPastTheFirstStep()
    {
        var overview = await Create([
            Aged("ot-1", "usr-ahmad", 5, ClaimStatus.REJECTED, currentStep: 1),
            Aged("ot-2", "usr-siti", 6, ClaimStatus.REJECTED, currentStep: 1),
        ]).GetAsync();

        Assert.Equal(2, overview.OverturnedSupervisors.Total);

        var sample = Assert.Single(overview.OverturnedSupervisors.Samples);
        Assert.Equal("Aisha Rahman", sample.SupervisorName);
        Assert.Equal(2, sample.OverturnedCount);
        Assert.Equal(2, sample.AffectedEmployees);
        Assert.Equal(["ot-1", "ot-2"], sample.ClaimIds);
    }

    [Fact]
    public async Task OverturnedSupervisors_IgnoreRejectionsAtTheFirstStep()
    {
        // Rejected at step 0: the first-line approver said no themselves, so
        // nothing was overturned.
        var overview = await Create([
            Aged("ot-1", "usr-ahmad", 5, ClaimStatus.REJECTED, currentStep: 0),
        ]).GetAsync();

        Assert.Equal(0, overview.OverturnedSupervisors.Total);
        Assert.Empty(overview.OverturnedSupervisors.Samples);
    }

    [Fact]
    public async Task OverturnedSupervisors_IgnoreRejectionsOlderThanNinetyDays()
    {
        var overview = await Create([
            Aged("old", "usr-ahmad", 120, ClaimStatus.REJECTED, currentStep: 1),
        ]).GetAsync();

        Assert.Equal(0, overview.OverturnedSupervisors.Total);
    }

    // ---- Module gating ----

    [Fact]
    public async Task ClaimsCards_AreEmptyWhenTheOrgHasNoClaimsModule()
    {
        var overview = await Create(
            [Aged("late", "usr-ahmad", 30), Aged("ot", "usr-ahmad", 5, ClaimStatus.REJECTED, 1)],
            router: null,
            modules: OrgModules.Attendance).GetAsync();

        Assert.Empty(overview.StalePendingClaims);
        Assert.Empty(overview.ProjectSpend);
        Assert.Equal(0, overview.OverturnedSupervisors.Total);
    }
}

internal sealed class FakeModuleAccessService : IModuleAccessService
{
    private readonly IReadOnlyCollection<string> _modules;
    private readonly IReadOnlyCollection<string> _ceiling;

    // `ceiling` is the org's plan alone; defaults to the same set as enabled.
    public FakeModuleAccessService(IReadOnlyCollection<string> modules, IReadOnlyCollection<string>? ceiling = null)
    {
        _modules = modules;
        _ceiling = ceiling ?? modules;
    }

    public Task<IReadOnlyCollection<string>> GetEnabledModulesAsync() => Task.FromResult(_modules);
    public Task<IReadOnlyCollection<string>> GetOrgModulesAsync() => Task.FromResult(_ceiling);
}

internal sealed class FakeWorkPermits(IReadOnlyList<WorkPermitDto> permits) : IEmployeeProfileService
{
    public Task<IReadOnlyList<WorkPermitDto>> GetWorkPermitsAsync() => Task.FromResult(permits);
    public Task<EmployeeProfileDto?> GetAsync(string userId) => throw new NotSupportedException();
    public Task<EmployeeProfileDto?> SaveAsync(string userId, EmployeeProfileDto dto) => throw new NotSupportedException();
}
