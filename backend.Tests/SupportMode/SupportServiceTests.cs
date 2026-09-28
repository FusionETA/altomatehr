using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Support;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Payroll;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.SupportMode;

// The Fusioneta support page's company list: every org on the platform, not
// just the caller's, with its owner and how many employees it runs.
public class SupportServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SupportService _service;

    public SupportServiceTests()
    {
        var currentUser = new StubCurrentUser { OrganizationId = "org-home" };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"support-{Guid.NewGuid()}").Options, currentUser);

        _service = new SupportService(
            new OrganizationRepository(_db),
            new StubPayrollOrganizations(),
            new OrganizationMembershipRepository(_db),
            new UserRepository(_db),
            new FakeAuditService(),
            currentUser);

        _db.Organizations.AddRange(
            new Organization { Id = "org-home", Name = "Fusioneta", CreatedAt = DateTime.UtcNow },
            new Organization { Id = "org-a", Name = "Alpha Sdn Bhd", CreatedAt = DateTime.UtcNow });
        _db.Users.AddRange(
            new User { Id = "u-owner", Email = "owner@alpha.example", Name = "Alpha Owner" },
            new User { Id = "u-1", Email = "e1@alpha.example", Name = "E1" },
            new User { Id = "u-2", Email = "e2@alpha.example", Name = "E2" });
        _db.OrganizationMemberships.AddRange(
            new OrganizationMembership { OrganizationId = "org-a", UserId = "u-owner", Role = "Owner" },
            new OrganizationMembership { OrganizationId = "org-a", UserId = "u-1", Role = "Employee" },
            new OrganizationMembership { OrganizationId = "org-a", UserId = "u-2", Role = "Supervisor" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // Seen from the caller's own org, the list still covers every org.
    [Fact]
    public async Task ListsEveryOrg_WithItsOwnerAndEmployeeCount()
    {
        var rows = await _service.ListOrganizationsAsync(null);

        Assert.Equal(["Alpha Sdn Bhd", "Fusioneta"], rows.Select(r => r.Name));
        var alpha = rows[0];
        Assert.Equal("owner@alpha.example", alpha.OwnerEmail);
        Assert.Equal(2, alpha.EmployeeCount);   // the Owner is not an employee
    }

    [Theory]
    [InlineData("alpha")]
    [InlineData("OWNER@ALPHA")]
    public async Task SearchesByNameOrOwnerEmail(string term)
    {
        var row = Assert.Single(await _service.ListOrganizationsAsync(term));
        Assert.Equal("org-a", row.Id);
    }
}
