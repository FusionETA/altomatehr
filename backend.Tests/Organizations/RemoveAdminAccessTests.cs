using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Organizations;

// Settings › Admins → Remove. For an admin who is admin only it takes them
// out of the company, as it always did. For an Employee & admin it removes
// only the admin access: they are still on the staff, still paid.
public class RemoveAdminAccessTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OrganizationService _service;

    public RemoveAdminAccessTests()
    {
        var caller = new StubCurrentUser { UserId = "usr-owner", Role = "Owner" };
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"remove-admin-{Guid.NewGuid()}").Options,
            caller);
        _db.Users.AddRange(
            new User { Id = "usr-hr", Email = "hr@example.test", Name = "Aminah" },
            new User { Id = "usr-acc", Email = "acc@example.test", Name = "Kelvin" });
        _db.OrganizationMemberships.AddRange(
            new OrganizationMembership
            {
                OrganizationId = "org-1", UserId = "usr-hr", Role = "Admin", EmploymentRole = "Supervisor",
                Modules = "payroll:view", PolicyScope = "pol-1", CanChangeSettings = false,
            },
            new OrganizationMembership { OrganizationId = "org-1", UserId = "usr-acc", Role = "Admin" });
        _db.SaveChanges();

        var memberships = new OrganizationMembershipRepository(_db);
        _service = new OrganizationService(
            new OrganizationRepository(_db), memberships, new FakeAuditService(), null!, null!,
            TestDirectory.Over(memberships, new UserRepository(_db)), null!, currentUser: caller);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task An_employee_and_admin_keeps_their_job()
    {
        var (notFound, error) = await _service.RemoveAdminAsync("usr-hr");

        Assert.False(notFound);
        Assert.Null(error);
        var hr = _db.OrganizationMemberships.Single(m => m.UserId == "usr-hr");
        Assert.Equal("Supervisor", hr.Role);
        Assert.True(hr.IsEmployed());
        // The old grant is cleared rather than left waiting.
        Assert.Null(hr.Modules);
        Assert.Null(hr.PolicyScope);
    }

    // Left as an Employee above the bottom layer, they would become the
    // approver for everyone below — and an Employee can't open approvals.
    [Fact]
    public async Task Removing_admin_access_is_refused_while_it_would_leave_an_employee_approving()
    {
        var hr = _db.OrganizationMemberships.Single(m => m.UserId == "usr-hr");
        hr.EmploymentRole = "Employee";
        _db.SaveChanges();
        var positions = new AltomateHR.Api.Tests.Auth.FakeApproverPositions();
        positions.Place("usr-hr", "Ops", layer: 1);
        var memberships = new OrganizationMembershipRepository(_db);
        var service = new OrganizationService(
            new OrganizationRepository(_db), memberships, new FakeAuditService(), null!, null!,
            TestDirectory.Over(memberships, new UserRepository(_db)), null!,
            currentUser: new StubCurrentUser { UserId = "usr-owner", Role = "Owner" }, approverPositions: positions);

        var (_, error) = await service.RemoveAdminAsync("usr-hr");

        Assert.Contains("Ops", error);
        Assert.Equal("Admin", _db.OrganizationMemberships.Single(m => m.UserId == "usr-hr").Role);
    }

    [Fact]
    public async Task An_admin_only_leaves_the_company()
    {
        await _service.RemoveAdminAsync("usr-acc");

        Assert.DoesNotContain(_db.OrganizationMemberships, m => m.UserId == "usr-acc");
    }

    [Fact]
    public async Task The_admin_list_says_who_is_also_on_the_staff()
    {
        var admins = await _service.ListAdminsAsync();

        Assert.Equal("Supervisor", admins.Single(a => a.UserId == "usr-hr").EmploymentRole);
        Assert.Null(admins.Single(a => a.UserId == "usr-acc").EmploymentRole);
    }
}
