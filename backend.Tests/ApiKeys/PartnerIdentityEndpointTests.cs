using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Modules.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AltomateHR.Api.Tests.ApiKeys;

// What a companion app (ABPay) calls: the identity endpoints with a master or
// org key, and the ordinary data endpoints with an org key. Over HTTP against
// the seeded demo company, so schemes, scopes and routing are all exercised.
public class PartnerIdentityEndpointTests
{
    private const string DemoOwner = "admin@altomate.com";
    private const string DemoEmployee = "employee@altomate.com";
    private const string DemoPassword = "password123";

    // ─── POST /auth/verify ──────────────────────────────────────────────

    [Fact]
    public async Task Verify_WithAMasterKey_ReturnsTheOwnerAndTheCompaniesTheyRun()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var response = await app.PostAsync("/auth/verify", master, new { email = DemoOwner, password = DemoPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("usr-admin", body.GetProperty("id").GetString());
        Assert.Equal(DemoOwner, body.GetProperty("email").GetString());
        Assert.Equal("Owner", body.GetProperty("role").GetString());
        var org = Assert.Single(body.GetProperty("organizations").EnumerateArray());
        Assert.Equal(await app.DemoOrgIdAsync(), org.GetProperty("id").GetString());
        Assert.False(string.IsNullOrEmpty(org.GetProperty("name").GetString()));
        Assert.Equal(org.GetProperty("name").GetString(), body.GetProperty("organizationName").GetString());
    }

    // Only the password is checked — the integration mints its own session.
    [Fact]
    public async Task Verify_IssuesNoSession()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var response = await app.PostAsync("/auth/verify", master, new { email = DemoOwner, password = DemoPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(0, await app.RefreshTokenCountAsync());
    }

    [Theory]
    [InlineData(DemoOwner, "wrong-password")]
    [InlineData(DemoEmployee, DemoPassword)]   // right password, but not an admin
    [InlineData("nobody@example.com", DemoPassword)]
    public async Task Verify_GivesOneAnswerForEveryFailure(string email, string password)
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var response = await app.PostAsync("/auth/verify", master, new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Invalid credentials for this organization.",
            (await Json(response)).GetProperty("message").GetString());
    }

    // ABPay tells "our app credential is bad" from "wrong password" by this
    // wording, so it must keep matching /invalid or revoked/.
    [Fact]
    public async Task Verify_AnUnknownMasterKey_SaysInvalidOrRevoked()
    {
        using var app = new App();

        var response = await app.PostAsync("/auth/verify", MasterTokenGenerator.Generate().Raw,
            new { email = DemoOwner, password = DemoPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid or revoked", (await Json(response)).GetProperty("message").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verify_WithAnOrgKey_StillWorksForAnAdminOfThatCompany()
    {
        using var app = new App();
        var key = await app.OrgKeyAsync();

        var response = await app.PostAsync("/auth/verify", key, new { email = DemoOwner, password = DemoPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(await app.DemoOrgIdAsync(), (await Json(response)).GetProperty("organizationId").GetString());
    }

    // ─── POST /auth/organizations ───────────────────────────────────────

    [Fact]
    public async Task Organizations_ByUserId_WithAMasterKey()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var response = await app.PostAsync("/auth/organizations", master, new { userId = "usr-admin" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var org = Assert.Single((await Json(response)).GetProperty("organizations").EnumerateArray());
        Assert.Equal(await app.DemoOrgIdAsync(), org.GetProperty("id").GetString());
        // The earlier field name is still there for existing callers.
        Assert.Equal(org.GetProperty("id").GetString(), org.GetProperty("organizationId").GetString());
    }

    [Fact]
    public async Task Organizations_ByEmail_WithAnOrgKey_OnlyForItsAdmins()
    {
        using var app = new App();
        var key = await app.OrgKeyAsync();

        var admin = await app.PostAsync("/auth/organizations", key, new { email = DemoOwner });
        var employee = await app.PostAsync("/auth/organizations", key, new { email = DemoEmployee });

        Assert.Single((await Json(admin)).GetProperty("organizations").EnumerateArray());
        Assert.Empty((await Json(employee)).GetProperty("organizations").EnumerateArray());
    }

    // ─── What else a companion reads, with an org key ───────────────────

    [Fact]
    public async Task WhoAmI_NamesTheKey()
    {
        using var app = new App();
        var key = await app.OrgKeyAsync("employees:read", "payroll:read");

        var body = await Json(await app.GetAsync("/whoami", key));

        Assert.Equal(await app.DemoOrgIdAsync(), body.GetProperty("organizationId").GetString());
        Assert.Equal("ABPay test", body.GetProperty("tokenName").GetString());
        Assert.Equal(["employees:read", "payroll:read"],
            body.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()));
    }

    [Theory]
    [InlineData("/employees")]
    [InlineData("/projects")]
    [InlineData("/payroll/runs")]
    public async Task AnOrgKey_ReadsTheDataEndpoints(string path)
    {
        using var app = new App();
        var key = await app.OrgKeyAsync("employees:read", "projects:read", "payroll:read");

        var response = await app.GetAsync(path, key);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, (await Json(response)).ValueKind);
    }

    // A master key confirms identities; it never reads a company's data.
    [Theory]
    [InlineData("/employees")]
    [InlineData("/payroll/runs")]
    public async Task AMasterKey_CannotReadData(string path)
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var response = await app.GetAsync(path, master);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── Harness ────────────────────────────────────────────────────────

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private sealed class App : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"altomatehr-partner-{Guid.NewGuid()}";
        private HttpClient? _client;

        private HttpClient Client => _client ??= CreateClient();

        public Task<HttpResponseMessage> PostAsync(string path, string token, object body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Client.SendAsync(request);
        }

        public Task<HttpResponseMessage> GetAsync(string path, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Client.SendAsync(request);
        }

        public async Task<string> DemoOrgIdAsync()
        {
            _ = Client;   // the host (and its seeder) starts with the first client
            using var scope = Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .OrganizationMemberships.IgnoreQueryFilters()
                .FirstAsync(m => m.UserId == "usr-admin")).OrganizationId;
        }

        public async Task<int> RefreshTokenCountAsync()
        {
            using var scope = Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
                .RefreshTokens.IgnoreQueryFilters().CountAsync();
        }

        public async Task<string> MasterKeyAsync()
        {
            _ = Client;
            var (raw, hash, prefix) = MasterTokenGenerator.Generate();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MasterKeys.Add(new MasterKey { Name = "ABPay", TokenHash = hash, TokenPrefix = prefix, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            return raw;
        }

        public async Task<string> OrgKeyAsync(params string[] scopes)
        {
            var orgId = await DemoOrgIdAsync();
            var (raw, hash, prefix) = ApiTokenGenerator.Generate();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ApiKeys.Add(new ApiKey
            {
                OrganizationId = orgId,
                Name = "ABPay test",
                TokenHash = hash,
                TokenPrefix = prefix,
                Scopes = ApiScopes.Join(scopes),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return raw;
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
