using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Tests.ApiKeys;

// FAH keys: short-lived wp_live_ keys that let the in-app assistant reach the
// API through the MCP server as the signed-in admin — their identity, their
// limits — instead of as an anonymous company key.
public class ActingKeyTests
{
    // ─── Issuing ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAdmin_GetsAKeyThatActsAsThem_AndExpiresSoon()
    {
        var repo = Repo(("org-1", "usr-admin", "Admin"));
        var service = new ActingKeyService(repo, new StubCurrentUser());

        var issued = await service.IssueAsync();

        Assert.StartsWith(ApiTokenGenerator.Prefix, issued.Token);
        var key = Assert.Single(repo.Keys);
        Assert.Equal(("usr-admin", "org-1"), (key.ActingUserId, key.OrganizationId));
        Assert.Equal(ApiTokenGenerator.HashToken(issued.Token), key.TokenHash);
        Assert.InRange(key.ExpiresAt!.Value, DateTime.UtcNow.AddMinutes(29), DateTime.UtcNow.AddMinutes(31));
        // Never the SSO door, and only the write areas FAH offers.
        Assert.DoesNotContain("sso:write", ApiScopes.Split(key.Scopes));
        Assert.DoesNotContain("claims:write", ApiScopes.Split(key.Scopes));
        Assert.Contains("payroll:write", ApiScopes.Split(key.Scopes));
    }

    // One key per chat turn: a second tab's turn must not end the first's key.
    [Fact]
    public async Task Keys_AreEndedOneAtATime()
    {
        var repo = Repo(("org-1", "usr-admin", "Owner"));
        var service = new ActingKeyService(repo, new StubCurrentUser());

        var first = await service.IssueAsync();
        var second = await service.IssueAsync();
        await service.RevokeAsync(first.Id);

        Assert.False(repo.Keys.Single(k => k.Id == first.Id).Active);
        Assert.True(repo.Keys.Single(k => k.Id == second.Id).Active);
    }

    [Theory]
    [InlineData("Employee")]
    [InlineData("Supervisor")]
    public async Task StaffCantHaveOne(string role)
    {
        var repo = Repo(("org-1", "usr-admin", role));
        var service = new ActingKeyService(repo, new StubCurrentUser());

        await Assert.ThrowsAsync<ActingKeyException>(() => service.IssueAsync());
        Assert.Empty(repo.Keys);
    }

    // Support acts as "System (Support)"; a key would put the staff member's
    // own name on the company's records.
    [Fact]
    public async Task SupportCantHaveOne()
    {
        var repo = Repo(("org-1", "usr-admin", "Admin"));
        var service = new ActingKeyService(repo, new StubCurrentUser { IsSupport = true });

        await Assert.ThrowsAsync<ActingKeyException>(() => service.IssueAsync());
    }

    [Fact]
    public async Task SomeoneRemovedFromTheCompany_CantHaveOne()
    {
        var service = new ActingKeyService(Repo(), new StubCurrentUser());

        await Assert.ThrowsAsync<ActingKeyException>(() => service.IssueAsync());
    }


    // ─── Authenticating with one ────────────────────────────────────────

    [Fact]
    public async Task AKey_AuthenticatesAsThePerson_AlwaysAsAdmin()
    {
        var repo = Repo(("org-1", "usr-admin", "Owner"));
        var token = (await new ActingKeyService(repo, new StubCurrentUser()).IssueAsync()).Token;

        var result = await AuthenticateAsync(repo, token);

        Assert.True(result.Succeeded);
        var user = result.Principal!;
        Assert.Equal("usr-admin", user.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("org-1", user.FindFirstValue("org"));
        Assert.Equal("admin@x.test", user.FindFirstValue(ClaimTypes.Email));
        // An Owner keeps full access through their membership, but owner-only
        // actions stay with a person on a screen.
        Assert.Equal("Admin", user.FindFirstValue(ClaimTypes.Role));
        // Still a key: [HumanOnly] routes refuse it.
        Assert.NotNull(user.FindFirstValue(ApiKeyAuthenticationDefaults.ApiKeyIdClaim));
        Assert.Equal("1", user.FindFirstValue(ApiKeyAuthenticationDefaults.ActingClaim));
    }

    [Fact]
    public async Task AnExpiredKey_IsRefused()
    {
        var repo = Repo(("org-1", "usr-admin", "Admin"));
        var token = (await new ActingKeyService(repo, new StubCurrentUser()).IssueAsync()).Token;
        repo.Keys.Single().ExpiresAt = DateTime.UtcNow.AddSeconds(-1);

        Assert.False((await AuthenticateAsync(repo, token)).Succeeded);
    }

    // Checked on every request, not only when the key was made.
    [Theory]
    [InlineData(null)]
    [InlineData("Employee")]
    public async Task AKey_StopsWorking_OnceThePersonIsNoLongerAnAdmin(string? nowRole)
    {
        var repo = Repo(("org-1", "usr-admin", "Admin"));
        var token = (await new ActingKeyService(repo, new StubCurrentUser()).IssueAsync()).Token;
        repo.Members.Remove(("org-1", "usr-admin"));
        if (nowRole is not null) repo.Members[("org-1", "usr-admin")] = new ActingMember(nowRole, "admin@x.test");

        Assert.False((await AuthenticateAsync(repo, token)).Succeeded);
    }

    [Fact]
    public async Task AnIntegrationKey_IsUnchanged()
    {
        var repo = Repo();
        var (raw, hash, prefix) = ApiTokenGenerator.Generate();
        repo.Keys.Add(new ApiKey { OrganizationId = "org-1", Name = "ABPay", TokenHash = hash, TokenPrefix = prefix, Scopes = "employees:read" });

        var user = (await AuthenticateAsync(repo, raw)).Principal!;

        Assert.StartsWith("apikey:", user.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Null(user.FindFirstValue(ApiKeyAuthenticationDefaults.ActingClaim));
    }

    // ─── Over HTTP, against the seeded demo company ─────────────────────

    [Fact]
    public async Task OverHttp_TheKeyReachesTheApi_ButNotOwnerOnlyOrPeopleOnlyRoutes()
    {
        using var app = new App();
        var token = await app.ActingKeyAsync("usr-admin");   // the demo Owner

        var whoami = await app.GetAsync("/whoami", token);
        Assert.Equal(HttpStatusCode.OK, whoami.StatusCode);
        using (var doc = JsonDocument.Parse(await whoami.Content.ReadAsStringAsync()))
            Assert.Equal("Admin", doc.RootElement.GetProperty("role").GetString());

        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/employees", token)).StatusCode);
        // Owner-only and [HumanOnly] — refused even though the person is the Owner.
        Assert.Equal(HttpStatusCode.Forbidden, (await app.GetAsync("/organizations/admins", token)).StatusCode);
    }

    [Fact]
    public async Task OverHttp_AnExpiredKey_Is401()
    {
        using var app = new App();
        var token = await app.ActingKeyAsync("usr-admin", expiresAt: DateTime.UtcNow.AddMinutes(-1));

        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetAsync("/employees", token)).StatusCode);
    }

    // Payroll write access also covers approve, submit, reject, revert, delete,
    // Xero sync and emailing payslips. None of those are FAH's: only its own
    // routes may change data.
    [Theory]
    [InlineData("POST", "/payroll/runs/run-1/approve")]
    [InlineData("POST", "/payroll/runs/run-1/submit")]
    [InlineData("POST", "/payroll/runs/run-1/revert")]
    [InlineData("DELETE", "/payroll/runs/run-1")]
    [InlineData("POST", "/payroll/runs/run-1/email-payslips")]
    public async Task OverHttp_AKeyCantChangeData_OutsideFahsOwnRoutes(string method, string path)
    {
        using var app = new App();
        var token = await app.ActingKeyAsync("usr-admin");

        var response = await app.SendAsync(method, path, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("FAH can't do this", await response.Content.ReadAsStringAsync());
    }

    // FAH's own write routes get through to the endpoint (which judges the body).
    [Fact]
    public async Task OverHttp_FahsOwnRoutes_AreLetThrough()
    {
        using var app = new App();
        var token = await app.ActingKeyAsync("usr-admin");

        var response = await app.SendAsync("POST", "/payroll/runs/run-does-not-exist/generate", token);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // An integration key is unaffected by FAH's limits.
    [Fact]
    public async Task OverHttp_AnIntegrationKey_IsNotHeldToFahsRoutes()
    {
        using var app = new App();
        var token = await app.IntegrationKeyAsync("payroll:read", "payroll:write");

        var response = await app.SendAsync("POST", "/payroll/runs/run-does-not-exist/approve", token);

        Assert.DoesNotContain("FAH can't do this", await response.Content.ReadAsStringAsync());
    }

    // ─── Harness ────────────────────────────────────────────────────────

    private static FakeApiKeyRepository Repo(params (string Org, string User, string Role)[] members)
    {
        var repo = new FakeApiKeyRepository();
        foreach (var (org, user, role) in members)
            repo.Members[(org, user)] = new ActingMember(role, "admin@x.test");
        return repo;
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(IApiKeyRepository repo, string token)
    {
        var handler = new ApiKeyAuthenticationHandler(
            new StaticOptions(), NullLoggerFactory.Instance, UrlEncoder.Default, repo);
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {token}";
        await handler.InitializeAsync(
            new AuthenticationScheme(ApiKeyAuthenticationDefaults.Scheme, null, typeof(ApiKeyAuthenticationHandler)),
            context);
        return await handler.AuthenticateAsync();
    }

    private sealed class StaticOptions : IOptionsMonitor<AuthenticationSchemeOptions>
    {
        public AuthenticationSchemeOptions CurrentValue { get; } = new();
        public AuthenticationSchemeOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<AuthenticationSchemeOptions, string?> listener) => null;
    }

    private sealed class App : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"altomatehr-acting-{Guid.NewGuid()}";
        private HttpClient? _client;

        private HttpClient Client => _client ??= CreateClient();

        public Task<HttpResponseMessage> GetAsync(string path, string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Client.SendAsync(request);
        }

        public Task<HttpResponseMessage> SendAsync(string method, string path, string token)
        {
            var request = new HttpRequestMessage(new HttpMethod(method), path)
            {
                Content = method == "GET" ? null : new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return Client.SendAsync(request);
        }

        public async Task<string> IntegrationKeyAsync(params string[] scopes)
        {
            _ = Client;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orgId = (await db.OrganizationMemberships.IgnoreQueryFilters()
                .FirstAsync(m => m.UserId == "usr-admin")).OrganizationId;
            var (raw, hash, prefix) = ApiTokenGenerator.Generate();
            db.ApiKeys.Add(new ApiKey
            {
                OrganizationId = orgId, Name = "Integration", TokenHash = hash, TokenPrefix = prefix,
                Scopes = ApiScopes.Join(scopes), CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
            return raw;
        }

        public async Task<string> ActingKeyAsync(string userId, DateTime? expiresAt = null)
        {
            _ = Client;   // the host (and its seeder) starts with the first client
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var orgId = (await db.OrganizationMemberships.IgnoreQueryFilters()
                .FirstAsync(m => m.UserId == userId)).OrganizationId;
            var (raw, hash, prefix) = ApiTokenGenerator.Generate();
            db.ApiKeys.Add(new ApiKey
            {
                OrganizationId = orgId,
                Name = "FAH",
                TokenHash = hash,
                TokenPrefix = prefix,
                Scopes = ApiScopes.Join(ApiKeyAuthenticationHandler.ActingScopes),
                CreatedAt = DateTime.UtcNow,
                ActingUserId = userId,
                ExpiresAt = expiresAt ?? DateTime.UtcNow.AddMinutes(30),
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
