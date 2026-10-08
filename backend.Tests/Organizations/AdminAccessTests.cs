using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AltomateHR.Api.Tests.Organizations;

// The Owner's three limits on an Admin — module View/Manage, employees in
// scope (by policy), and the settings switch — and the gates that enforce them.
public class AdminAccessTests
{
    // ─── Grant format ───────────────────────────────────────────────────

    [Fact]
    public void A_plain_key_is_manage_and_a_view_suffix_is_view()
    {
        var grant = OrgModules.ParseGrant("payroll:view,leave,bogus");

        Assert.Equal(ModuleLevel.View, grant["payroll"]);
        Assert.Equal(ModuleLevel.Manage, grant["leave"]);
        Assert.False(grant.ContainsKey("bogus"));
        Assert.False(grant.ContainsKey("claims"));   // absent = Off
    }

    [Fact]
    public void Formatting_round_trips_and_drops_off_modules()
    {
        var csv = OrgModules.FormatGrant(new Dictionary<string, ModuleLevel>
        {
            ["payroll"] = ModuleLevel.Manage,
            ["employees"] = ModuleLevel.View,
            ["claims"] = ModuleLevel.None,
        });

        var back = OrgModules.ParseGrant(csv);
        Assert.Equal(2, back.Count);
        Assert.Equal(ModuleLevel.Manage, back["payroll"]);
        Assert.Equal(ModuleLevel.View, back["employees"]);
    }

    // ─── Resolving the caller ───────────────────────────────────────────

    [Fact]
    public async Task An_admin_gets_their_levels_scope_and_settings_switch()
    {
        using var db = Db();
        AddOrg(db);
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = "org-1", UserId = "usr-admin", Role = "Admin",
            Modules = "payroll:view,leave", PolicyScope = "pol-site", CanChangeSettings = false,
        });
        db.SaveChanges();

        var access = Service(db);
        var mine = await access.GetAccessAsync();

        Assert.Equal(ModuleLevel.View, await access.GetModuleLevelAsync("payroll"));
        Assert.Equal(ModuleLevel.Manage, await access.GetModuleLevelAsync("leave"));
        Assert.Equal(ModuleLevel.None, await access.GetModuleLevelAsync("claims"));
        Assert.False(mine.CanChangeSettings);
        Assert.False(mine.HasFullEmployeeScope);
        Assert.Equal(["pol-site"], mine.PolicyScope!);
    }

    [Fact]
    public async Task An_owner_is_never_limited_whatever_the_columns_hold()
    {
        using var db = Db();
        AddOrg(db);
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = "org-1", UserId = "usr-admin", Role = "Owner",
            Modules = "payroll:view", PolicyScope = "pol-site", CanChangeSettings = false,
        });
        db.SaveChanges();

        var access = Service(db, role: "Owner");
        var mine = await access.GetAccessAsync();

        Assert.Equal(ModuleLevel.Manage, await access.GetModuleLevelAsync("payroll"));
        Assert.True(mine.CanChangeSettings);
        Assert.True(mine.HasFullEmployeeScope);
    }

    // Removed by the Owner while their access token is still live: nothing,
    // not everything. An API key (no membership by design) is unaffected.
    [Theory]
    [InlineData("usr-gone", ModuleLevel.None)]
    [InlineData("apikey:key-1", ModuleLevel.Manage)]
    public async Task An_admin_without_a_membership_gets_nothing_unless_it_is_a_key(string userId, ModuleLevel expected)
    {
        using var db = Db();
        AddOrg(db);
        db.SaveChanges();

        var access = new ModuleAccessService(
            new OrganizationRepository(db),
            TestDirectory.Over(new OrganizationMembershipRepository(db)),
            new StubCurrentUser { UserId = userId, Role = "Admin" });

        Assert.Equal(expected, await access.GetModuleLevelAsync("payroll"));
    }

    // GetCallerLevelAsync, for a service reading another module's data: an API
    // key (full grant) is capped by that module's scopes, read off the real
    // CurrentUser claims — no :read → None, :read → View, both → Manage. A
    // write scope alone does not let a key read.
    [Theory]
    [InlineData(new string[0], ModuleLevel.None)]
    [InlineData(new[] { "employees:write" }, ModuleLevel.None)]
    [InlineData(new[] { "employees:read" }, ModuleLevel.View)]
    [InlineData(new[] { "employees:read", "employees:write" }, ModuleLevel.Manage)]
    public async Task An_api_keys_caller_level_is_capped_by_its_scopes(string[] scopes, ModuleLevel expected)
    {
        using var db = Db();
        AddOrg(db);
        db.SaveChanges();

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, "apikey:key-1"),
            new(System.Security.Claims.ClaimTypes.Role, "Admin"),
            new("org", "org-1"),
            new(AltomateHR.Api.Modules.ApiKeys.ApiKeyAuthenticationDefaults.ApiKeyIdClaim, "key-1"),
        };
        claims.AddRange(scopes.Select(s =>
            new System.Security.Claims.Claim(AltomateHR.Api.Modules.ApiKeys.ApiKeyAuthenticationDefaults.ScopeClaim, s)));
        var http = new DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "ApiKey")),
        };
        var user = new CurrentUser(new HttpContextAccessor { HttpContext = http });

        var access = new ModuleAccessService(
            new OrganizationRepository(db), TestDirectory.Over(new OrganizationMembershipRepository(db)), user);

        Assert.True(user.IsScopedMachine);
        Assert.Equal(ModuleLevel.Manage, await access.GetModuleLevelAsync("employees"));   // the grant alone
        Assert.Equal(expected, await access.GetCallerLevelAsync("employees"));
    }

    [Fact]
    public async Task A_people_callers_level_is_their_grant_whatever_scopes_say()
    {
        using var db = Db();
        AddOrg(db);
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = "org-1", UserId = "usr-admin", Role = "Admin", Modules = "employees:view,documents",
        });
        db.SaveChanges();

        var access = Service(db);

        Assert.Equal(ModuleLevel.View, await access.GetCallerLevelAsync("employees"));
        Assert.Equal(ModuleLevel.Manage, await access.GetCallerLevelAsync("documents"));
        Assert.Equal(ModuleLevel.None, await access.GetCallerLevelAsync("payroll"));
    }

    // ─── Gates ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("GET", false, true)]
    [InlineData("POST", false, false)]
    [InlineData("PUT", false, false)]
    [InlineData("POST", true, true)]   // a [ReadOnlyAction] POST is a read
    public async Task View_level_allows_reads_only(string method, bool readOnlyAction, bool allowed)
    {
        var access = new FixedAccess(new AdminAccess(true,
            new Dictionary<string, ModuleLevel> { ["payroll"] = ModuleLevel.View }, true, null));
        object[] metadata = readOnlyAction
            ? [new AuthorizeAttribute { Roles = "Admin,Owner" }, new ReadOnlyActionAttribute()]
            : [new AuthorizeAttribute { Roles = "Admin,Owner" }];

        var result = await Run(new RequireModuleAttribute("payroll"), access, method, metadata);

        Assert.Equal(allowed, result is null);
    }

    [Theory]
    [InlineData("GET", true)]
    [InlineData("PUT", false)]
    public async Task The_settings_switch_blocks_changes_not_reads(string method, bool allowed)
    {
        var access = new FixedAccess(new AdminAccess(true, null, CanChangeSettings: false, null));

        var result = await Run(new RequireSettingsAttribute(), access, method);

        Assert.Equal(allowed, result is null);
    }

    [Fact]
    public async Task A_limited_scope_cannot_act_company_wide_and_cannot_download_a_filing()
    {
        var limited = new FixedAccess(new AdminAccess(true, null, true, ["pol-site"]));

        Assert.Null(await Run(new RequireFullEmployeeScopeAttribute(), limited, "GET"));          // view a run
        Assert.NotNull(await Run(new RequireFullEmployeeScopeAttribute(), limited, "POST"));      // generate
        Assert.NotNull(await Run(new RequireFullEmployeeScopeAttribute { IncludeReads = true }, limited, "GET"));  // EPF file

        var full = new FixedAccess(AdminAccess.Full);
        Assert.Null(await Run(new RequireFullEmployeeScopeAttribute { IncludeReads = true }, full, "GET"));
    }

    [Fact]
    public async Task Someone_outside_the_scope_is_not_found()
    {
        var scope = new EmployeeScope();
        scope.Limit(["usr-in"], ["prof-in"]);

        Assert.Null(await Run(new EmployeeInScopeAttribute("id"), new FixedAccess(AdminAccess.Full), "GET", [], scope, ("id", "usr-in")));
        Assert.Null(await Run(new EmployeeInScopeAttribute("id"), new FixedAccess(AdminAccess.Full), "GET", [], scope, ("id", "prof-in")));
        Assert.IsType<NotFoundResult>(
            await Run(new EmployeeInScopeAttribute("id"), new FixedAccess(AdminAccess.Full), "GET", [], scope, ("id", "usr-out")));
    }

    // Global: any route naming an employee — leave entitlements, on-behalf
    // leave, team members, payroll adjustments — is out of reach outside scope.
    [Theory]
    [InlineData("employeeId", "usr-out", true)]
    [InlineData("userId", "usr-out", true)]
    [InlineData("employeeProfileId", "prof-out", true)]
    [InlineData("employeeId", "usr-in", false)]
    public async Task Every_route_naming_an_employee_is_scoped(string key, string value, bool notFound)
    {
        var scope = new EmployeeScope();
        scope.Limit(["usr-in"], ["prof-in"]);

        var result = await Run(new EmployeeScopeRouteFilter(), new FixedAccess(AdminAccess.Full), "PUT", [], scope, (key, value));

        Assert.Equal(notFound, result is NotFoundResult);
    }

    // ─── The database filter ────────────────────────────────────────────

    [Fact]
    public void A_limited_scope_filters_employee_rows_and_leaves_others_alone()
    {
        var name = $"scope-{Guid.NewGuid()}";
        using (var seed = Db(name))
        {
            seed.Claims.AddRange(
                new Claim { OrganizationId = "org-1", EmployeeId = "usr-in", Title = "in" },
                new Claim { OrganizationId = "org-1", EmployeeId = "usr-out", Title = "out" });
            seed.EmployeeProfiles.AddRange(
                new EmployeeProfile { Id = "prof-in", OrganizationId = "org-1", UserId = "usr-in" },
                new EmployeeProfile { Id = "prof-out", OrganizationId = "org-1", UserId = "usr-out" });
            seed.SaveChanges();
        }

        var scope = new EmployeeScope();
        scope.Limit(["usr-in"], ["prof-in"]);
        using var limited = Db(name, scope);
        Assert.Equal(["in"], limited.Claims.Select(c => c.Title).ToList());
        Assert.Equal(["prof-in"], limited.EmployeeProfiles.Select(p => p.Id).ToList());

        using var unlimited = Db(name);
        Assert.Equal(2, unlimited.Claims.Count());
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static AppDbContext Db(string? name = null, IEmployeeScope? scope = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(name ?? $"access-{Guid.NewGuid()}")
                .Options,
            new StubCurrentUser(),
            scope);

    private static void AddOrg(AppDbContext db) =>
        db.Organizations.Add(new Organization { Id = "org-1", Name = "Org", Plan = OrgPlan.DIY, Tier = OrgPlanTier.PAID });

    private static ModuleAccessService Service(AppDbContext db, string role = "Admin") =>
        new(new OrganizationRepository(db),
            TestDirectory.Over(new OrganizationMembershipRepository(db)),
            new StubCurrentUser { Role = role });

    private static async Task<IActionResult?> Run(
        IAsyncActionFilter filter, IModuleAccessService access, string method,
        object[]? metadata = null, IEmployeeScope? scope = null, (string Key, string Value)? route = null)
    {
        var services = new ServiceCollection()
            .AddSingleton(access)
            .AddSingleton(scope ?? new EmployeeScope())
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = method;
        var routeData = new RouteData();
        if (route is { } r) routeData.Values[r.Key] = r.Value;

        var actionContext = new ActionContext(
            http, routeData, new ActionDescriptor { EndpointMetadata = metadata ?? [] });
        var context = new ActionExecutingContext(
            actionContext, [], new Dictionary<string, object?>(), controller: new object());

        var reached = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        });

        return reached ? null : context.Result;
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
