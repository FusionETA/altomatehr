using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AltomateHR.Api.Tests.Employees;

// An Employee & admin can't change their own employee record from the admin
// portal (OwnRecordGuard): no raising their own pay, no adjusting their own
// payroll or leave. Everyone else's record, reads, the Owner, and an admin
// who is admin only are untouched.
public class OwnRecordGuardTests : IDisposable
{
    private readonly AppDbContext _db;

    public OwnRecordGuardTests()
    {
        _db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase($"own-record-{Guid.NewGuid()}").Options,
            new StubCurrentUser());
        _db.OrganizationMemberships.AddRange(
            new OrganizationMembership { OrganizationId = "org-1", UserId = "usr-hr", Role = "Admin", EmploymentRole = "Employee" },
            new OrganizationMembership { OrganizationId = "org-1", UserId = "usr-acc", Role = "Admin" },
            new OrganizationMembership { OrganizationId = "org-1", UserId = "usr-boss", Role = "Owner", EmploymentRole = "Employee" },
            new OrganizationMembership { OrganizationId = "org-1", UserId = "usr-emp", Role = "Employee" });
        _db.EmployeeProfiles.AddRange(
            new EmployeeProfile { Id = "prof-hr", OrganizationId = "org-1", UserId = "usr-hr" },
            new EmployeeProfile { Id = "prof-emp", OrganizationId = "org-1", UserId = "usr-emp" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("employeeId", "usr-hr")]
    [InlineData("userId", "usr-hr")]
    [InlineData("employeeProfileId", "prof-hr")]   // payroll adjustments are keyed by profile
    public async Task An_employee_and_admin_cannot_change_their_own_record(string key, string value)
    {
        var result = await Run(new OwnRecordRouteFilter(), caller: "usr-hr", role: "Admin", "PUT", (key, value));

        var refused = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, refused.StatusCode);
    }

    [Fact]
    public async Task The_employee_record_s_own_id_route_is_guarded_too()
    {
        var result = await Run(new OwnRecordGuardAttribute("id"), caller: "usr-hr", role: "Admin", "PUT", ("id", "usr-hr"));

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Theory]
    [InlineData("usr-hr", "Admin", "GET", "usr-hr")]       // reading their own record
    [InlineData("usr-hr", "Admin", "PUT", "usr-emp")]      // someone else's record
    [InlineData("usr-hr", "Admin", "PUT", "prof-emp")]
    [InlineData("usr-hr", "Employee", "PUT", "usr-hr")]    // in their own employee portal
    [InlineData("usr-acc", "Admin", "PUT", "usr-acc")]     // admin only: no employee record
    [InlineData("usr-boss", "Owner", "PUT", "usr-boss")]   // the owner sets their own
    public async Task Everything_else_passes(string caller, string role, string method, string target)
    {
        var key = target.StartsWith("prof-") ? "employeeProfileId" : "employeeId";

        Assert.Null(await Run(new OwnRecordRouteFilter(), caller, role, method, (key, target)));
    }

    [Fact]
    public async Task Support_is_never_refused()
    {
        Assert.Null(await Run(new OwnRecordRouteFilter(), "usr-hr", "Admin", "PUT", ("employeeId", "usr-hr"), support: true));
    }

    private async Task<IActionResult?> Run(
        IAsyncActionFilter filter, string caller, string role, string method, (string Key, string Value) route,
        bool support = false)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICurrentUser>(new StubCurrentUser { UserId = caller, Role = role, IsSupport = support })
            .AddSingleton<IDirectoryService>(TestDirectory.Over(
                new OrganizationMembershipRepository(_db), profiles: new EmployeeProfileRepository(_db)))
            .AddScoped<IOwnRecordRule, OwnRecordRule>()
            .BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Method = method;
        var routeData = new RouteData();
        routeData.Values[route.Key] = route.Value;
        var actionContext = new ActionContext(http, routeData, new ActionDescriptor { EndpointMetadata = [] });
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
}
