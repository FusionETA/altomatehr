using System.Reflection;
using System.Security.Claims;
using AltomateHR.Api.Modules.ApiKeys;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;

namespace AltomateHR.Api.Tests.ApiKeys;

// A wp_live_ key acts as an Admin of its company, and scopes only narrow the
// endpoints that opt in. Every payroll endpoint used to opt out — any key, even
// one granted only "employees:read", could approve or delete a payroll run — and
// a key could reach the API-keys screen and mint itself a key with every scope.
public class EndpointScopeGuardTests
{
    private static readonly Assembly Api = typeof(ApiScopes).Assembly;

    private static IEnumerable<MethodInfo> Actions(Type controller) =>
        controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => a is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute));

    // Structural: a new payroll endpoint added without a scope fails here rather
    // than quietly opening payroll to every key.
    [Fact]
    public void EveryPayrollEndpointDeclaresAPayrollScope()
    {
        var payrollControllers = Api.GetTypes()
            .Where(t => t.Namespace == "AltomateHR.Api.Modules.Payroll"
                        && typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();
        Assert.NotEmpty(payrollControllers);

        var unscoped = payrollControllers
            .SelectMany(c => Actions(c).Select(a => (c, a)))
            .Where(x => !x.a.GetCustomAttributes<RequireScopeAttribute>()
                .Any(s => s.Scope is "payroll:read" or "payroll:write"))
            .Select(x => $"{x.c.Name}.{x.a.Name}")
            .ToList();

        Assert.Empty(unscoped);
    }

    // Reads need read, anything that changes something needs write.
    [Fact]
    public void PayrollReadsNeedReadAndChangesNeedWrite()
    {
        var wrong = Api.GetTypes()
            .Where(t => t.Namespace == "AltomateHR.Api.Modules.Payroll" && typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(Actions)
            .Where(a =>
            {
                var isGet = a.GetCustomAttributes().Any(x => x is HttpGetAttribute);
                var scope = a.GetCustomAttributes<RequireScopeAttribute>().Single().Scope;
                return isGet ? scope != "payroll:read" : scope != "payroll:write";
            })
            .Select(a => $"{a.DeclaringType!.Name}.{a.Name}")
            .ToList();

        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("AltomateHR.Api.Modules.ApiKeys.ApiKeysController")]
    [InlineData("AltomateHR.Api.Modules.Xero.XeroController")]
    [InlineData("AltomateHR.Api.Modules.Audit.AuditController")]
    public void AdminToolsRefuseKeys(string controller)
    {
        var type = Api.GetType(controller);
        Assert.NotNull(type);
        Assert.NotNull(type!.GetCustomAttribute<HumanOnlyAttribute>());
    }

    [Fact]
    public void PayrollWriteIsAGrantableScope() => Assert.True(ApiScopes.IsKnown("payroll:write"));

    // ---- the HumanOnly filter itself ----

    private static async Task<IActionResult?> Run(HumanOnlyAttribute filter, params Claim[] claims)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        var context = new ActionExecutingContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            [], new Dictionary<string, object?>(), controller: new object());
        var reached = false;
        await filter.OnActionExecutionAsync(context, () =>
        {
            reached = true;
            return Task.FromResult(new ActionExecutedContext(context, [], controller: new object()));
        });
        return reached ? null : context.Result;
    }

    [Fact]
    public async Task HumanOnly_RefusesACompanyKey()
    {
        var result = await Run(new HumanOnlyAttribute(),
            new Claim(ApiKeyAuthenticationDefaults.ApiKeyIdClaim, "key-1"));

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task HumanOnly_RefusesAPartnerToken()
    {
        var result = await Run(new HumanOnlyAttribute(), new Claim("partner_client", "appraisify"));

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task HumanOnly_LetsASignedInPersonThrough()
    {
        var result = await Run(new HumanOnlyAttribute(), new Claim(ClaimTypes.Role, "Admin"));

        Assert.Null(result);
    }
}
