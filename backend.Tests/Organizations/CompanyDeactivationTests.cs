using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AltomateHR.Api.Tests.Organizations;

// A superadmin deactivates a company: everyone in it is shut out at once —
// sessions, sign-in, switching — and someone who is also in another company
// carries on there. Reactivating brings it all back.
public class CompanyDeactivationTests
{
    private const string Admin = "admin@altomate.com";   // the demo Owner, and the superadmin here
    private const string Employee = "employee@altomate.com";
    private const string Password = "password123";
    private const string CustomerOwner = "owner@customer.test";

    [Fact]
    public async Task ASessionInADeactivatedCompany_IsRefusedAtOnce_AndWorksAgainOnReactivation()
    {
        using var app = new App();
        using var support = app.CreateClient();
        await SignInAsync(support, Admin);
        var customer = await CustomerCompanyAsync(support);

        // The customer's owner, signed in to their only company.
        using var inside = app.CreateClient();
        await SignInAsync(inside, CustomerOwner);
        Assert.Equal(HttpStatusCode.OK, (await inside.GetAsync("/organizations/current")).StatusCode);

        var deactivated = await support.PostAsJsonAsync($"/support/organizations/{customer}/deactivate", new { reason = "Unpaid since Sep" });
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var row = await Json(deactivated);
        Assert.Equal("Unpaid since Sep", row.GetProperty("deactivatedReason").GetString());
        Assert.Equal(Admin, row.GetProperty("deactivatedBy").GetString());

        var refused = await inside.GetAsync("/organizations/current");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(CompanyDeactivatedMiddleware.Code, (await Json(refused)).GetProperty("code").GetString());

        // No other company to move to: refreshing ends the session, and
        // signing in again says why.
        Assert.Equal(HttpStatusCode.Unauthorized, (await inside.PostAsync("/auth/refresh", null)).StatusCode);
        using var again = app.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await again.PostAsJsonAsync("/auth/login", new { email = CustomerOwner, password = Password })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await support.PostAsync($"/support/organizations/{customer}/reactivate", null)).StatusCode);
        await SignInAsync(again, CustomerOwner);
        Assert.Equal(HttpStatusCode.OK, (await again.GetAsync("/organizations/current")).StatusCode);
    }

    // A superadmin would be shut out with it, Reactivate button and all.
    [Fact]
    public async Task ACompanyASuperadminIsIn_CantBeDeactivated()
    {
        using var app = new App();
        using var support = app.CreateClient();
        await SignInAsync(support, Admin);
        var own = (await Json(await support.GetAsync("/organizations/current"))).GetProperty("id").GetString()!;
        var alsoTheirs = await CreateCompanyAsync(support, "Second Co");

        var ownRefused = await support.PostAsJsonAsync($"/support/organizations/{own}/deactivate", new { reason = "test" });
        var theirsRefused = await support.PostAsJsonAsync($"/support/organizations/{alsoTheirs}/deactivate", new { reason = "test" });

        Assert.Equal(HttpStatusCode.BadRequest, ownRefused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, theirsRefused.StatusCode);
        Assert.Contains("superadmin", (await Json(theirsRefused)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task SignIn_ToOnlyADeactivatedCompany_SaysSo_AndSomeoneInAnotherLandsThere()
    {
        using var app = new App();
        using var admin = app.CreateClient();
        await SignInAsync(admin, Admin);
        var home = (await Json(await admin.GetAsync("/organizations/current"))).GetProperty("id").GetString()!;
        var second = await CreateCompanyAsync(admin, "Second Co");

        await app.DeactivateAsync(home);

        using var employee = app.CreateClient();
        var refused = await employee.PostAsJsonAsync("/auth/login", new { email = Employee, password = Password });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Contains("deactivated", (await Json(refused)).GetProperty("message").GetString());

        // A wrong password is still just a wrong password.
        var wrong = await employee.PostAsJsonAsync("/auth/login", new { email = Employee, password = "nope" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var both = app.CreateClient();
        var landed = await Json(await both.PostAsJsonAsync("/auth/login", new { email = Admin, password = Password }));
        Assert.Equal(second, landed.GetProperty("activeOrganizationId").GetString());
    }

    [Fact]
    public async Task Deactivating_NeedsAReason()
    {
        using var app = new App();
        using var support = app.CreateClient();
        await SignInAsync(support, Admin);
        var customer = await CustomerCompanyAsync(support);

        var response = await support.PostAsJsonAsync($"/support/organizations/{customer}/deactivate", new { reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Helpers ----

    private static async Task SignInAsync(HttpClient client, string email)
    {
        var res = await client.PostAsJsonAsync("/auth/login", new { email, password = Password });
        res.EnsureSuccessStatusCode();
        UseToken(client, await Json(res));
    }

    private static async Task SwitchAsync(HttpClient client, string organizationId)
    {
        var res = await client.PostAsync($"/auth/switch-org/{organizationId}", null);
        res.EnsureSuccessStatusCode();
        UseToken(client, await Json(res));
    }

    private static async Task<string> CreateCompanyAsync(HttpClient client, string name)
    {
        var res = await client.PostAsJsonAsync("/organizations", new { name });
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode} {res.Headers.WwwAuthenticate} {await res.Content.ReadAsStringAsync()}");
        return (await Json(res)).GetProperty("id").GetString()!;
    }

    // A customer's company, set up by support, that no superadmin is in.
    private static async Task<string> CustomerCompanyAsync(HttpClient support)
    {
        var res = await support.PostAsJsonAsync("/support/organizations", new
        {
            orgName = "Customer Co", ownerName = "Customer Owner", ownerEmail = CustomerOwner, password = Password,
        });
        Assert.True(res.IsSuccessStatusCode, await res.Content.ReadAsStringAsync());
        return (await Json(res)).GetProperty("organizationId").GetString()!;
    }

    private static void UseToken(HttpClient client, JsonElement auth) =>
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth.GetProperty("token").GetString());

    private static async Task<JsonElement> Json(HttpResponseMessage res) =>
        (await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync())).RootElement.Clone();

    private sealed class App : WebApplicationFactory<Program>
    {
        private const string JwtKey = "test-jwt-key-that-is-long-enough-for-hmac-sha256-signing";
        private readonly string _databaseName = $"altomatehr-tests-{Guid.NewGuid()}";

        // Straight to the database, for a company the superadmin is in themselves.
        public async Task DeactivateAsync(string organizationId)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var org = await db.Organizations.SingleAsync(o => o.Id == organizationId);
            org.DeactivatedAt = DateTime.UtcNow;
            org.DeactivatedReason = "test";
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IOrganizationStatus>().Changed();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            // Read while the app starts (the JWT validation key), so set early
            // too — otherwise a developer's user-secrets key validates instead.
            builder.UseSetting("Jwt:Key", JwtKey);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=localhost;Database=tests;User=test;Password=test",
                    ["Jwt:Key"] = JwtKey,
                    ["Jwt:Issuer"] = "altomatehr-api",
                    ["Jwt:Audience"] = "altomatehr-client",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "7",
                    ["Seed:DemoData"] = "true",
                    ["SUPERADMIN_EMAILS"] = Admin,
                    ["Bitrix:WebhookUrl"] = "",
                });
            });
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            });
        }
    }
}
