using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Policies;
using AltomateHR.Api.Modules.Policies.Dtos;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Policies;

// Policies aren't a per-admin grant. Every admin edits the policies their
// employee scope covers; creating, archiving and the default reach the whole
// company, so they are the Owner's (and an API key's).
public class PolicyAdminAccessTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();

    public PolicyAdminAccessTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"policy-access-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _db.EmployeePolicies.AddRange(
            new EmployeePolicy { Id = "pol-office", OrganizationId = "org-1", Name = "Office", IsDefault = true },
            new EmployeePolicy { Id = "pol-site", OrganizationId = "org-1", Name = "Site" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private PolicyService As(AdminAccess access) => new(
        new EmployeePolicyRepository(_db),
        new PolicyLeaveEntitlementRepository(_db),
        TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new UserRepository(_db),
            new EmployeeProfileRepository(_db)),
        new FixedAccess(access));

    private static readonly AdminAccess Owner = AdminAccess.Full;
    private static readonly AdminAccess AllEmployeesAdmin = new(true, null, true, null);
    private static readonly AdminAccess SiteAdmin = new(true, null, true, ["pol-site"]);

    private static SavePolicyDto Named(string name) => new() { Name = name };

    [Fact]
    public async Task AnAdminLimitedToSomePolicies_SeesAndEditsOnlyThose()
    {
        var service = As(SiteAdmin);

        Assert.Equal(["Site"], (await service.GetVisibleAsync()).Select(p => p.Name));
        Assert.True((await service.UpdateAsync("pol-site", Named("Site crew"))).Ok);

        // Someone else's policy is one they can't see: a 404, not a 403.
        var other = await service.UpdateAsync("pol-office", Named("Mine now"));
        Assert.False(other.Ok);
        Assert.False(other.Forbidden);
        Assert.Null(other.Error);
        Assert.Equal("Office", (await _db.EmployeePolicies.FindAsync("pol-office"))!.Name);
    }

    [Fact]
    public async Task AnAdminWhoSeesEveryone_EditsEveryPolicy_ButCannotCreateArchiveOrSetTheDefault()
    {
        var service = As(AllEmployeesAdmin);

        Assert.Equal(2, (await service.GetVisibleAsync()).Count());
        Assert.True((await service.UpdateAsync("pol-office", Named("Office staff"))).Ok);

        foreach (var refused in new[]
        {
            await service.CreateAsync(Named("Drivers")),
            await service.SetDefaultAsync("pol-site"),
            await service.SetArchivedAsync("pol-site", true),
            await service.SetArchivedAsync("pol-site", false),
        })
        {
            Assert.False(refused.Ok);
            Assert.True(refused.Forbidden);
            Assert.Contains("owner", refused.Error, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(2, await _db.EmployeePolicies.CountAsync());
        Assert.True((await _db.EmployeePolicies.FindAsync("pol-office"))!.IsDefault);
    }

    [Fact]
    public async Task TheOwner_CreatesArchivesAndSetsTheDefault()
    {
        var service = As(Owner);

        Assert.True((await service.CreateAsync(Named("Drivers"))).Ok);
        Assert.True((await service.SetDefaultAsync("pol-site")).Ok);
        Assert.True((await service.SetArchivedAsync("pol-office", true)).Ok);
        Assert.Equal(3, (await service.GetVisibleAsync()).Count());
    }

    // Policies is not something the Owner grants per admin: it isn't offered,
    // and an admin whose grant leaves it out still has it.
    [Fact]
    public void Policies_IsNotAPerAdminGrant()
    {
        Assert.DoesNotContain(OrgModules.Policies, OrgModules.GrantableModules);
        Assert.Contains(OrgModules.Payroll, OrgModules.GrantableModules);
    }

    private sealed class FixedAccess(AdminAccess access) : IModuleAccessService
    {
        public Task<IReadOnlyCollection<string>> GetEnabledModulesAsync() =>
            Task.FromResult<IReadOnlyCollection<string>>(OrgModules.AllModules.ToList());
        public Task<IReadOnlyCollection<string>> GetOrgModulesAsync() => GetEnabledModulesAsync();
        public Task<AdminAccess> GetAccessAsync() => Task.FromResult(access);
        public Task<ModuleLevel> GetModuleLevelAsync(string module) => Task.FromResult(access.LevelFor(module));
    }
}
