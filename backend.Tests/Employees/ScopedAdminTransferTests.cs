using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Employees;

// A policy-limited admin can Transfer / Duplicate inside their limits: only
// staff in their scope at the source, and only onto policies their scope
// covers at the target. (They used to get no targets at all, so the buttons
// never showed.)
public class ScopedAdminTransferTests : IDisposable
{
    private const string Admin = "usr-admin";
    private const string Staff = "usr-staff";
    private const string OtherStaff = "usr-other";

    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new() { UserId = Admin, OrganizationId = "org-a", Role = "Admin" };
    private readonly EmployeeScope _scope = new();

    public ScopedAdminTransferTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"scoped-transfer-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser, _scope);

        _db.Organizations.AddRange(
            new Organization { Id = "org-a", Name = "Alpha" },
            new Organization { Id = "org-b", Name = "Bravo" });
        _db.EmployeePolicies.AddRange(
            new EmployeePolicy { Id = "a-pol", OrganizationId = "org-a", Name = "A Monthly", IsDefault = true },
            new EmployeePolicy { Id = "b-in", OrganizationId = "org-b", Name = "B Monthly", IsDefault = true },
            new EmployeePolicy { Id = "b-out", OrganizationId = "org-b", Name = "B Directors" });
        _db.OrganizationMemberships.AddRange(
            new OrganizationMembership { OrganizationId = "org-a", UserId = Admin, Role = "Admin", PolicyScope = "a-pol" },
            new OrganizationMembership { OrganizationId = "org-b", UserId = Admin, Role = "Admin", PolicyScope = "b-in" },
            new OrganizationMembership { OrganizationId = "org-a", UserId = Staff, Role = "Employee", PolicyId = "a-pol" },
            new OrganizationMembership { OrganizationId = "org-a", UserId = OtherStaff, Role = "Employee", PolicyId = "a-pol" });
        _db.SaveChanges();

        // As EmployeeScopeMiddleware would resolve it: the admin and Staff only.
        _scope.Limit([Admin, Staff], []);
    }

    public void Dispose() => _db.Dispose();

    private EmployeeTransferService Service()
    {
        var memberships = new OrganizationMembershipRepository(_db);
        var profiles = new EmployeeProfileRepository(_db);
        return new EmployeeTransferService(
            new EmployeeTransferRepository(_db),
            memberships,
            profiles,
            new EmployeePolicyRepository(_db),
            new OrganizationRepository(_db),
            new PayslipRepository(_db),
            new UserRepository(_db),
            new FakeAuditService(),
            _currentUser,
            new EmploymentHistory(new EmploymentPeriodRepository(_db), profiles, memberships),
            scope: _scope);
    }

    [Fact]
    public async Task A_scoped_admin_gets_targets_cut_down_to_their_policies()
    {
        var options = await Service().GetOptionsAsync(Staff);

        var target = Assert.Single(options!.Targets);
        Assert.Equal("org-b", target.Id);
        Assert.Equal(["b-in"], target.Policies.Select(p => p.Id));
    }

    [Fact]
    public async Task Someone_outside_their_scope_is_not_found()
    {
        var service = Service();

        Assert.Null(await service.GetOptionsAsync(OtherStaff));

        var duplicate = await service.DuplicateAsync(OtherStaff,
            new DuplicateEmployeeDto { TargetOrganizationId = "org-b", TargetPolicyId = "b-in", JoinDate = DateTime.UtcNow.Date });
        Assert.False(duplicate.Ok);
        Assert.Null(duplicate.Error);   // 404

        var transfer = await service.CreateAsync(OtherStaff,
            new CreateEmployeeTransferDto { TargetOrganizationId = "org-b", TargetPolicyId = "b-in", EffectiveDate = DateTime.UtcNow.Date.AddDays(7) });
        Assert.False(transfer.Ok);
        Assert.Null(transfer.Error);   // 404
    }

    [Fact]
    public async Task A_policy_outside_their_target_scope_is_refused()
    {
        var (ok, _, error) = await Service().DuplicateAsync(Staff,
            new DuplicateEmployeeDto { TargetOrganizationId = "org-b", TargetPolicyId = "b-out", JoinDate = DateTime.UtcNow.Date });

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Null(await _db.OrganizationMemberships.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.OrganizationId == "org-b" && m.UserId == Staff));
    }

    [Fact]
    public async Task A_policy_inside_their_scope_goes_through()
    {
        var (ok, _, error) = await Service().DuplicateAsync(Staff,
            new DuplicateEmployeeDto { TargetOrganizationId = "org-b", TargetPolicyId = "b-in", JoinDate = DateTime.UtcNow.Date });

        Assert.True(ok, error);
        var placed = await _db.OrganizationMemberships.IgnoreQueryFilters()
            .SingleAsync(m => m.OrganizationId == "org-b" && m.UserId == Staff);
        Assert.Equal("b-in", placed.PolicyId);
    }
}
