using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AltomateHR.Api.Tests.Provisioning;

// A company made through the API has nobody in it; POST /admin/admins gives
// people seats. The first seat is Owner, so an API-made company is never left
// without one; every later seat is Admin.
public class OrgAdminEndpointTests
{
    [Fact]
    public async Task TheFirstPersonIsOwner_EveryoneAfterIsAdmin()
    {
        using var app = new App();
        var key = await app.ProvisionAsync("Rymun Test Sdn Bhd");

        var first = await app.GrantAsync(key, "first@example.com");
        var second = await app.GrantAsync(key, "second@example.com");

        Assert.Equal("Owner", first.GetProperty("role").GetString());
        Assert.True(first.GetProperty("created").GetBoolean());
        Assert.True(first.GetProperty("linked").GetBoolean());
        Assert.Equal("Admin", second.GetProperty("role").GetString());
    }

    // Retrying the SSO hand-off re-sends the grant; nothing changes.
    [Fact]
    public async Task GrantingAgain_IsANoOp_AndKeepsTheSeat()
    {
        using var app = new App();
        var key = await app.ProvisionAsync("Retry Test Sdn Bhd");
        await app.GrantAsync(key, "first@example.com");

        var again = await app.GrantAsync(key, "first@example.com");

        Assert.Equal("Owner", again.GetProperty("role").GetString());
        Assert.False(again.GetProperty("created").GetBoolean());
        Assert.False(again.GetProperty("linked").GetBoolean());
        Assert.Equal(1, await app.SeatsAsync("Retry Test Sdn Bhd"));
    }

    // A company that already has an administrator keeps its people as they
    // are: a newcomer is an Admin even when nobody is the Owner.
    [Fact]
    public async Task ACompanyWithAnAdminButNoOwner_GivesNewcomersAdmin()
    {
        using var app = new App();
        var key = await app.ProvisionAsync("Admins Only Sdn Bhd");
        await app.AddMemberAsync("Admins Only Sdn Bhd", "usr-emp", "Admin");

        var grant = await app.GrantAsync(key, "newcomer@example.com");

        Assert.Equal("Admin", grant.GetProperty("role").GetString());
    }

    // Promoting an existing employee of a company nobody administers makes
    // them its Owner.
    [Fact]
    public async Task PromotingAnEmployee_OfACompanyWithNoAdministrator_MakesThemOwner()
    {
        using var app = new App();
        var key = await app.ProvisionAsync("Promote Sdn Bhd");
        await app.AddMemberAsync("Promote Sdn Bhd", "usr-emp", "Employee");

        var grant = await app.GrantAsync(key, "employee@altomate.com");

        Assert.Equal("Owner", grant.GetProperty("role").GetString());
        Assert.False(grant.GetProperty("created").GetBoolean());
        Assert.True(grant.GetProperty("linked").GetBoolean());
    }

    [Fact]
    public async Task ListingAdmins_ShowsEachSeat()
    {
        using var app = new App();
        var key = await app.ProvisionAsync("List Sdn Bhd");
        await app.GrantAsync(key, "first@example.com");
        await app.GrantAsync(key, "second@example.com");

        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/users?role=OWNER,ADMIN");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var body = await Json(await app.Client.SendAsync(request));

        var rows = body.GetProperty("data").EnumerateArray()
            .Select(r => (r.GetProperty("email").GetString(), r.GetProperty("role").GetString()))
            .ToList();
        Assert.Equal([("first@example.com", "Owner"), ("second@example.com", "Admin")], rows);
    }

    // ─── Harness ────────────────────────────────────────────────────────

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private sealed class App : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"altomatehr-orgadmins-{Guid.NewGuid()}";
        private HttpClient? _client;

        public HttpClient Client => _client ??= CreateClient();

        // POST /admin/organizations with a master key; returns the new company's key.
        public async Task<string> ProvisionAsync(string name)
        {
            _ = Client;
            var (raw, hash, prefix) = MasterTokenGenerator.Generate();
            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.MasterKeys.Add(new MasterKey { Name = "test", TokenHash = hash, TokenPrefix = prefix, CreatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/admin/organizations") { Content = JsonContent.Create(new { name }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", raw);
            return (await Json(await Client.SendAsync(request))).GetProperty("apiKey").GetString()!;
        }

        public async Task<JsonElement> GrantAsync(string key, string email)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/admin/admins") { Content = JsonContent.Create(new { email }) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            return await Json(await Client.SendAsync(request));
        }

        public async Task AddMemberAsync(string orgName, string userId, string role)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var org = await db.Organizations.SingleAsync(o => o.Name == orgName);
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = userId, Role = role, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task<int> SeatsAsync(string orgName)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var org = await db.Organizations.SingleAsync(o => o.Name == orgName);
            return await db.OrganizationMemberships.IgnoreQueryFilters().CountAsync(m => m.OrganizationId == org.Id);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Server=localhost;Database=tests;User=test;Password=test",
                    ["Jwt:Key"] = "test-jwt-key-that-is-long-enough-for-hmac-sha256-signing",
                    ["Jwt:Issuer"] = "altomatehr-api",
                    ["Jwt:Audience"] = "altomatehr-client",
                    ["Jwt:AccessTokenMinutes"] = "15",
                    ["Jwt:RefreshTokenDays"] = "7",
                    ["Seed:DemoData"] = "true",
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
