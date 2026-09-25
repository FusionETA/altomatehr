using System.Reflection;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace AltomateHR.Api.Tests.Organizations;

// The Owner's "Manage access" grant. It used to narrow almost nothing: only
// Claims and Attendance checked it, Payroll and the activity log could not be
// withheld at all, and where it WAS checked it also locked an Admin out of
// their own claims. These pin what it now means.
public class ModuleAccessTests
{
    // ─── Who a grant applies to ─────────────────────────────────────────

    [Fact]
    public async Task AnAdminsGrantNarrowsThem()
    {
        var access = Access(role: "Admin", grant: "projects,employees");

        var enabled = await access.GetEnabledModulesAsync();

        Assert.Contains(OrgModules.Employees, enabled);
        Assert.DoesNotContain(OrgModules.Payroll, enabled);
        Assert.DoesNotContain(OrgModules.Audit, enabled);
    }

    // The employee form can set the column for any role, and a demoted Admin
    // keeps it. It must not narrow someone with no admin surface to narrow.
    [Theory]
    [InlineData("Employee")]
    [InlineData("Supervisor")]
    [InlineData("Owner")]
    public async Task AGrantOnANonAdminIsIgnored(string role)
    {
        var access = Access(role, grant: "projects");

        var enabled = await access.GetEnabledModulesAsync();

        Assert.Contains(OrgModules.Payroll, enabled);
        Assert.Contains(OrgModules.Leave, enabled);
    }

    // Payroll and the activity log are core, as in the previous system: every
    // plan includes them, FREE too, so adding them takes nothing away.
    [Fact]
    public void PayrollAndAuditAreOnEveryPlan()
    {
        var free = OrgModules.DeriveOrgEnabledModules(OrgPlan.DIY, OrgPlanTier.FREE, []);

        Assert.Contains(OrgModules.Payroll, free);
        Assert.Contains(OrgModules.Audit, free);
        Assert.True(OrgModules.IsKnownModule(OrgModules.Payroll));
    }

    // ─── The endpoint gate ──────────────────────────────────────────────

    // Filing a claim carries no role restriction: an Admin whose grant leaves
    // Claims out still files their own.
    [Fact]
    public async Task ASelfServiceEndpointOnlyNeedsThePlan()
    {
        var result = await Run("claims",
            ceiling: [OrgModules.Claims], enabled: [],
            metadata: [new AuthorizeAttribute()]);

        Assert.Null(result);
    }

    [Fact]
    public async Task AnAdminEndpointNeedsTheGrant()
    {
        var result = await Run("payroll",
            ceiling: [OrgModules.Payroll], enabled: [OrgModules.Employees],
            metadata: [new AuthorizeAttribute { Roles = "Admin,Owner" }]);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, forbidden.StatusCode);
        Assert.Contains("admin access", Message(forbidden));
    }

    // Supervisor approvals are role-restricted too, so an Admin without the
    // grant cannot approve org-wide through them. A Supervisor has no grant.
    [Fact]
    public async Task ASupervisorEndpointNeedsTheGrantOfAnAdmin()
    {
        var result = await Run("leave",
            ceiling: [OrgModules.Leave], enabled: [],
            metadata: [new AuthorizeAttribute { Roles = "Supervisor,Admin,Owner" }]);

        Assert.IsType<ObjectResult>(result);
    }

    [Fact]
    public async Task AModuleThePlanLacksIsRefusedEvenForSelfService()
    {
        var result = await Run("claims",
            ceiling: [OrgModules.Leave], enabled: [OrgModules.Leave],
            metadata: [new AuthorizeAttribute()]);

        var forbidden = Assert.IsType<ObjectResult>(result);
        Assert.Contains("plan does not include", Message(forbidden));
    }

    // A lookup other screens borrow — the employee list behind the claims
    // filter — is held to the plan only.
    [Fact]
    public async Task AnExemptLookupOnlyNeedsThePlan()
    {
        var result = await Run("employees",
            ceiling: [OrgModules.Employees], enabled: [OrgModules.Claims],
            metadata: [new AuthorizeAttribute { Roles = "Admin,Owner" }, new ModuleGrantExemptAttribute()]);

        Assert.Null(result);
    }

    [Fact]
    public async Task AGrantedAdminPasses()
    {
        var result = await Run("payroll",
            ceiling: [OrgModules.Payroll], enabled: [OrgModules.Payroll],
            metadata: [new AuthorizeAttribute { Roles = "Admin,Owner" }]);

        Assert.Null(result);
    }

    // ─── Coverage ───────────────────────────────────────────────────────

    // Every payroll controller carries the payroll gate. A new one added
    // without it would hand every Admin salaries and bank details again.
    // Payslips is the employee's own and is exempt.
    [Fact]
    public void EveryPayrollAdminControllerIsGatedByPayroll()
    {
        var ungated = typeof(OrgModules).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => t.GetCustomAttribute<RouteAttribute>()?.Template.StartsWith("payroll") == true)
            .Where(t => !t.GetCustomAttributes<RequireModuleAttribute>().Any(a => Module(a) == OrgModules.Payroll))
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(ungated);
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static ModuleAccessService Access(string role, string? grant) =>
        new(
            new OneOrg(new Organization { Id = "org-1", Plan = OrgPlan.DIY, Tier = OrgPlanTier.PAID }),
            new OneMembership(new OrganizationMembership
            {
                OrganizationId = "org-1", UserId = "usr-admin", Role = role, Modules = grant,
            }),
            new StubCurrentUser());

    private static async Task<IActionResult?> Run(
        string module, string[] ceiling, string[] enabled, object[] metadata)
    {
        var services = new ServiceCollection()
            .AddSingleton<IModuleAccessService>(new FakeModuleAccessService(enabled, ceiling))
            .BuildServiceProvider();

        var actionContext = new ActionContext(
            new DefaultHttpContext { RequestServices = services },
            new RouteData(),
            new ActionDescriptor { EndpointMetadata = metadata });
        var context = new ActionExecutingContext(
            actionContext, [], new Dictionary<string, object?>(), controller: new object());

        var reached = false;
        await new RequireModuleAttribute(module).OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        });

        return reached ? null : context.Result;
    }

    private static string Message(ObjectResult result) =>
        System.Text.Json.JsonSerializer.Serialize(result.Value);

    private static string? Module(RequireModuleAttribute attribute) =>
        typeof(RequireModuleAttribute)
            .GetField("_module", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(attribute) as string;

    private sealed class OneOrg(Organization org) : IOrganizationRepository
    {
        public Task<Organization?> GetByIdAsync(string id) => Task.FromResult<Organization?>(org.Id == id ? org : null);
        public Task<Organization?> GetFirstAsync() => throw new NotSupportedException();
        public Task<List<Organization>> GetAllAsync() => throw new NotSupportedException();
        public Task AddAsync(Organization organization) => throw new NotSupportedException();
        public Task UpdateAsync(Organization organization) => throw new NotSupportedException();
        public Task<bool> AnyAsync() => throw new NotSupportedException();
    }

    private sealed class OneMembership(OrganizationMembership membership) : IDirectoryService
    {
        public Task<OrganizationMembership?> GetMembershipForUserAsync(string userId) =>
            Task.FromResult<OrganizationMembership?>(membership.UserId == userId ? membership : null);

        public Task<List<User>> GetUsersAsync() => throw new NotSupportedException();
        public Task<List<OrganizationMembership>> GetMembershipsForCurrentOrgAsync() => throw new NotSupportedException();
        public Task<OrganizationMembership?> GetMembershipAsync(string organizationId, string userId) => throw new NotSupportedException();
        public Task<List<OrganizationMembership>> GetMembershipsByUserAsync(string userId) => throw new NotSupportedException();
        public Task<int> CountMembershipsByShiftAsync(string shiftId) => throw new NotSupportedException();
        public Task<List<EmployeeProfile>> GetProfilesForCurrentOrgAsync() => throw new NotSupportedException();
        public Task<User?> GetUserAsync(string id) => throw new NotSupportedException();
    }
}
