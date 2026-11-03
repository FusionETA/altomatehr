using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Organizations;

// The Owner edits an admin's name and login email from the Admins page. The
// email signs that person in everywhere, so it may only change when every
// company the login opens is one this owner owns.
public class AdminDetailsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new() { UserId = "usr-owner", OrganizationId = "org-1", Role = "Owner" };
    private readonly FakeAuditService _audit = new();

    public AdminDetailsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"admin-details-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _db.Users.AddRange(
            new User { Id = "usr-owner", Email = "owner@x.com", Name = "Owner", PasswordHash = "x" },
            new User { Id = "usr-admin", Email = "admin@x.com", Name = "Admin", PasswordHash = "x" },
            new User { Id = "usr-other", Email = "taken@x.com", Name = "Other", PasswordHash = "x" });
        _db.OrganizationMemberships.AddRange(
            Seat("org-1", "usr-owner", "Owner"),
            Seat("org-2", "usr-owner", "Owner"),
            Seat("org-1", "usr-admin", "Admin"),
            Seat("org-2", "usr-admin", "Admin"));
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static OrganizationMembership Seat(string org, string user, string role) =>
        new() { OrganizationId = org, UserId = user, Role = role };

    private OrganizationService Service() => new(
        null!, new OrganizationMembershipRepository(_db), _audit, null!, null!, null!, null!,
        currentUser: _currentUser, users: new UserRepository(_db));

    private static UpdateAdminDetailsDto Details(string name, string email) => new() { Name = name, Email = email };

    [Fact]
    public async Task TheOwnerOfEveryCompanyTheLoginOpens_ChangesItsNameAndEmail()
    {
        var (notFound, error, admin) = await Service().UpdateAdminDetailsAsync("usr-admin", Details(" Cleve ", "cleve@x.com"));

        Assert.False(notFound);
        Assert.Null(error);
        Assert.Equal("cleve@x.com", admin!.Email);
        var user = await _db.Users.FindAsync("usr-admin");
        Assert.Equal(("Cleve", "cleve@x.com"), (user!.Name, user.Email));
        Assert.Single(_audit.Written);
    }

    // A login that also opens someone else's company keeps its email: changing
    // it here would hand that company to whoever owns the new address.
    [Fact]
    public async Task ALoginSharedWithAnotherOwnersCompany_KeepsItsEmail_ButTheNameCanChange()
    {
        _db.OrganizationMemberships.Add(Seat("org-3", "usr-admin", "Admin"));
        await _db.SaveChangesAsync();

        var refused = await Service().UpdateAdminDetailsAsync("usr-admin", Details("Admin", "mine@x.com"));
        Assert.Contains("support", refused.Error);
        Assert.Equal("admin@x.com", (await _db.Users.FindAsync("usr-admin"))!.Email);

        var renamed = await Service().UpdateAdminDetailsAsync("usr-admin", Details("Renamed", "ADMIN@x.com"));
        Assert.Null(renamed.Error);
        Assert.Equal("Renamed", (await _db.Users.FindAsync("usr-admin"))!.Name);

        _currentUser.IsSupport = true;
        Assert.Null((await Service().UpdateAdminDetailsAsync("usr-admin", Details("Admin", "mine@x.com"))).Error);
    }

    [Fact]
    public async Task AnEmailAnotherAccountUses_IsRefused()
    {
        var (_, error, _) = await Service().UpdateAdminDetailsAsync("usr-admin", Details("Admin", "taken@x.com"));

        Assert.Contains("already used", error);
        Assert.Equal("admin@x.com", (await _db.Users.FindAsync("usr-admin"))!.Email);
    }

    // Only Admins are edited here; the Owner's own row is read-only.
    [Fact]
    public async Task TheOwner_IsNotEditedHere()
    {
        Assert.True((await Service().UpdateAdminDetailsAsync("usr-owner", Details("Boss", "boss@x.com"))).NotFound);
    }
}
