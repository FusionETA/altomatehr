using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Policies.Entities;
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

    // A companion refreshes from ONE server address, so /auth/organizations
    // must not sit in the 5/min login bucket it shares with /auth/login and
    // /auth/verify: six admins opening ABPay in a minute would throttle the
    // sixth, and a 429's ProblemDetails body has no `message` field, so it
    // reads to the caller as a failed sign-in rather than a rate limit.
    [Fact]
    public async Task Organizations_IsNotOnTheLoginRateLimit()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            var response = await app.PostAsync("/auth/organizations", master, new { userId = "usr-admin" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ─── What else a companion reads, with an org key ───────────────────

    [Fact]
    public async Task WhoAmI_NamesTheKey()
    {
        using var app = new App();
        var key = await app.OrgKeyAsync("employees:read", "payroll:read");

        var body = await Json(await app.GetAsync("/whoami", key));

        Assert.Equal("key", body.GetProperty("kind").GetString());
        Assert.Equal(await app.DemoOrgIdAsync(), body.GetProperty("organizationId").GetString());
        Assert.Equal("ABPay test", body.GetProperty("tokenName").GetString());
        Assert.Equal(["employees:read", "payroll:read"],
            body.GetProperty("scopes").EnumerateArray().Select(s => s.GetString()));
    }

    // The master key belongs to no company, so it can read no company — which
    // is exactly why it must still be able to ask what this deployment
    // supports before relying on an endpoint.
    [Fact]
    public async Task WhoAmI_AnswersAMasterKeyWithTheFeatureList()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var response = await app.GetAsync("/whoami", master);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.Equal("master", body.GetProperty("kind").GetString());
        Assert.Contains(ApiFeatures.AuthAccess,
            body.GetProperty("features").EnumerateArray().Select(f => f.GetString()));
        // It belongs to no company, so it is told about none.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("organizationId").ValueKind);
        Assert.Empty(body.GetProperty("scopes").EnumerateArray());
    }

    // [AllowAnonymous] on that action must not open it to everyone.
    [Fact]
    public async Task WhoAmI_StillRefusesACallerWithNoCredential()
    {
        using var app = new App();

        var response = await app.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/whoami"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhoAmI_StillRefusesAnUnknownKey()
    {
        using var app = new App();

        var response = await app.GetAsync("/whoami", ApiTokenGenerator.Generate().Raw);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

    // ─── POST /auth/access ──────────────────────────────────────────────
    //
    // The endpoint exists because a companion holds the COMPANY's key, which
    // sees everyone. These assert that what it hands back is the admin's own
    // Manage access, in the ids the data endpoints use.

    [Fact]
    public async Task Access_ForAnOwner_SaysEveryEmployeeAndSendsNoList()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var body = await Json(await app.PostAsync("/auth/access", master,
            new { userId = "usr-admin", organizationId = await app.DemoOrgIdAsync() }));

        Assert.Equal("Owner", body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("allEmployees").GetBoolean());
        Assert.True(body.GetProperty("canRunPayroll").GetBoolean());
        // Absent, not an empty array — an empty list means "nobody".
        Assert.False(body.TryGetProperty("employees", out _));
    }

    // The acceptance case: an admin limited to one policy sees only the people
    // on it, and the ids line up with what the companion already reads.
    [Fact]
    public async Task Access_ForALimitedAdmin_IsOnlyTheirPeople()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();
        var orgId = await app.DemoOrgIdAsync();

        var contract = await app.AddPolicyAsync("Contract");
        await app.PutOnPolicyAsync("usr-emp", contract);
        await app.MakeAdminAsync("usr-limited", policyScope: contract);

        var body = await Json(await app.PostAsync("/auth/access", master,
            new { userId = "usr-limited", organizationId = orgId }));

        Assert.Equal("Admin", body.GetProperty("role").GetString());
        Assert.False(body.GetProperty("allEmployees").GetBoolean());

        var userIds = body.GetProperty("employees").EnumerateArray()
            .Select(e => e.GetProperty("userId").GetString()).ToList();

        // Evan is on Contract; Sara is on the default policy, so she is not.
        Assert.Contains("usr-emp", userIds);
        Assert.DoesNotContain("usr-super", userIds);
        // The admin themselves, exactly as AltomateHR's own GET /employees
        // shows them to themselves.
        Assert.Contains("usr-limited", userIds);
    }

    // "`userId` must be the same id GET /employees returns as `id`, and
    // `employeeProfileId` the same as GET /payroll/employees" — asserted
    // against the live responses rather than against how they are built.
    [Fact]
    public async Task Access_UsesTheSameIdsTheDataEndpointsReturn()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();
        var key = await app.OrgKeyAsync("employees:read", "payroll:read");

        var contract = await app.AddPolicyAsync("Contract");
        await app.PutOnPolicyAsync("usr-emp", contract);
        // Someone whose payroll details were never saved: they are on the
        // roster under a BLANK stand-in profile, so only userId can match them.
        await app.AddStaffWithoutAPayrollProfileAsync("usr-newbie", contract);
        await app.MakeAdminAsync("usr-limited", policyScope: contract);

        var access = await Json(await app.PostAsync("/auth/access", master,
            new { userId = "usr-limited", organizationId = await app.DemoOrgIdAsync() }));
        var people = access.GetProperty("employees").EnumerateArray().ToList();

        var employeeIds = (await Json(await app.GetAsync("/employees", key)))
            .EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToHashSet();
        var roster = (await Json(await app.GetAsync("/payroll/employees", key)))
            .EnumerateArray().ToList();
        var rosterUserIds = roster.Select(e => e.GetProperty("userId").GetString()).ToHashSet();
        var profileIds = roster
            .Where(e => e.GetProperty("hasPayrollProfile").GetBoolean())
            .Select(e => e.GetProperty("employeeProfileId").GetString()).ToHashSet();

        Assert.NotEmpty(people);
        foreach (var person in people)
        {
            // userId is what BOTH endpoints are matched by.
            Assert.Contains(person.GetProperty("userId").GetString(), employeeIds);

            var profileId = person.GetProperty("employeeProfileId").GetString();
            if (profileId is not null) Assert.Contains(profileId, profileIds);
        }

        // The person with no payroll record is in scope, carries no profile
        // id, and is still on the roster — matched by userId.
        var newbie = Assert.Single(people, p => p.GetProperty("userId").GetString() == "usr-newbie");
        Assert.Null(newbie.GetProperty("employeeProfileId").GetString());
        Assert.Contains("usr-newbie", rosterUserIds);

        // And it really is narrower than the company key's own view.
        Assert.True(people.Count < employeeIds.Count);
    }

    // No caching: the Owner narrows an admin at 10:00 and the companion must
    // honour it at 10:01.
    [Fact]
    public async Task Access_ReflectsAChangeToManageAccessOnTheNextCall()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();
        var orgId = await app.DemoOrgIdAsync();

        var contract = await app.AddPolicyAsync("Contract");
        await app.MakeAdminAsync("usr-limited", policyScope: contract);

        var limited = await Json(await app.PostAsync("/auth/access", master,
            new { userId = "usr-limited", organizationId = orgId }));
        Assert.False(limited.GetProperty("allEmployees").GetBoolean());

        await app.MakeAdminAsync("usr-limited", policyScope: null);   // "all employees"

        var lifted = await Json(await app.PostAsync("/auth/access", master,
            new { userId = "usr-limited", organizationId = orgId }));
        Assert.True(lifted.GetProperty("allEmployees").GetBoolean());
        Assert.True(lifted.GetProperty("canRunPayroll").GetBoolean());
    }

    // Zero employees in scope is an answer, not a failure.
    [Fact]
    public async Task Access_AnAdminWhoseScopeCoversNobody_IsAnEmptyList()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var unused = await app.AddPolicyAsync("Nobody is on this");
        await app.MakeAdminAsync("usr-limited", policyScope: unused);

        var body = await Json(await app.PostAsync("/auth/access", master,
            new { userId = "usr-limited", organizationId = await app.DemoOrgIdAsync() }));

        Assert.False(body.GetProperty("allEmployees").GetBoolean());
        // Only the admin themselves.
        var person = Assert.Single(body.GetProperty("employees").EnumerateArray());
        Assert.Equal("usr-limited", person.GetProperty("userId").GetString());
    }

    // A company they don't administer is invisible on both endpoints.
    [Fact]
    public async Task Access_ForACompanyTheyDoNotAdminister_Is404()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();
        var otherOrgId = await app.AddOrganizationAsync("Someone Else Sdn Bhd");

        var response = await app.PostAsync("/auth/access", master,
            new { userId = "usr-admin", organizationId = otherOrgId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var organizations = (await Json(await app.PostAsync("/auth/organizations", master,
            new { userId = "usr-admin" }))).GetProperty("organizations").EnumerateArray();
        Assert.DoesNotContain(otherOrgId, organizations.Select(o => o.GetProperty("id").GetString()));
    }

    // An unauthenticated caller learns that the key is bad, not what the body
    // should look like.
    [Fact]
    public async Task Access_ChecksTheKeyBeforeTheBody()
    {
        using var app = new App();

        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/access")
        {
            Content = JsonContent.Create(new { }),
        };
        var response = await app.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Access_ForAnUnknownUser_Is404()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var response = await app.PostAsync("/auth/access", master,
            new { userId = "usr-nobody", organizationId = await app.DemoOrgIdAsync() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // A company key may read its own company in full — but asking what another
    // PERSON may reach would make it a directory of who is restricted.
    [Fact]
    public async Task Access_RefusesACompanyKey()
    {
        using var app = new App();
        var key = await app.OrgKeyAsync("employees:read");

        var response = await app.PostAsync("/auth/access", key,
            new { userId = "usr-admin", organizationId = await app.DemoOrgIdAsync() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ─── The per-company flags on the identity endpoints ────────────────

    [Fact]
    public async Task Verify_CarriesEachCompanysAccessFlags()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var org = Assert.Single((await Json(await app.PostAsync("/auth/verify", master,
            new { email = DemoOwner, password = DemoPassword }))).GetProperty("organizations").EnumerateArray());

        Assert.Equal("Owner", org.GetProperty("role").GetString());
        Assert.True(org.GetProperty("allEmployees").GetBoolean());
        Assert.True(org.GetProperty("canRunPayroll").GetBoolean());
    }

    [Fact]
    public async Task Organizations_SaysWhereAnAdminIsLimited()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        var contract = await app.AddPolicyAsync("Contract");
        await app.MakeAdminAsync("usr-limited", policyScope: contract);

        var org = Assert.Single((await Json(await app.PostAsync("/auth/organizations", master,
            new { userId = "usr-limited" }))).GetProperty("organizations").EnumerateArray());

        Assert.Equal("Admin", org.GetProperty("role").GetString());
        Assert.False(org.GetProperty("allEmployees").GetBoolean());
        // A run covers the whole company, so a limited admin can only look.
        Assert.False(org.GetProperty("canRunPayroll").GetBoolean());
    }

    // An admin without the Payroll module can't post a run either, however
    // many employees they cover.
    [Fact]
    public async Task Access_AnAdminWithoutThePayrollModule_CannotRunPayroll()
    {
        using var app = new App();
        var master = await app.MasterKeyAsync();

        await app.MakeAdminAsync("usr-limited", policyScope: null, modules: "employees,projects");

        var body = await Json(await app.PostAsync("/auth/access", master,
            new { userId = "usr-limited", organizationId = await app.DemoOrgIdAsync() }));

        Assert.True(body.GetProperty("allEmployees").GetBoolean());
        Assert.False(body.GetProperty("canRunPayroll").GetBoolean());
        Assert.False(body.GetProperty("modules").TryGetProperty("payroll", out _));
        Assert.True(body.GetProperty("modules").TryGetProperty("projects", out _));
    }

    // These endpoints take a bare email and NO password, so telling a company
    // key which of its admins are restricted would hand it the directory that
    // /auth/access refuses it.
    [Fact]
    public async Task Organizations_TellsACompanyKeyNothingAboutAPersonsLimits()
    {
        using var app = new App();
        var key = await app.OrgKeyAsync();

        var contract = await app.AddPolicyAsync("Contract");
        await app.MakeAdminAsync("usr-limited", policyScope: contract);

        var org = Assert.Single((await Json(await app.PostAsync("/auth/organizations", key,
            new { userId = "usr-limited" }))).GetProperty("organizations").EnumerateArray());

        // The role was always there; the limits are not, and must not read as
        // "unrestricted" either.
        Assert.Equal("Admin", org.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, org.GetProperty("allEmployees").ValueKind);
        Assert.Equal(JsonValueKind.Null, org.GetProperty("canRunPayroll").ValueKind);
    }

    [Fact]
    public async Task Verify_TellsACompanyKeyNothingAboutAPersonsLimits()
    {
        using var app = new App();
        var key = await app.OrgKeyAsync();

        var org = Assert.Single((await Json(await app.PostAsync("/auth/verify", key,
            new { email = DemoOwner, password = DemoPassword }))).GetProperty("organizations").EnumerateArray());

        Assert.Equal(JsonValueKind.Null, org.GetProperty("allEmployees").ValueKind);
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

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request) => Client.SendAsync(request);

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

        // An Admin in the demo company with the given Manage access. Creating
        // them the way the Owner's screen does — a membership row with the two
        // columns set — so the test exercises the real resolution.
        public async Task MakeAdminAsync(string userId, string? policyScope, string? modules = null)
        {
            var orgId = await DemoOrgIdAsync();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId))
            {
                db.Users.Add(new AltomateHR.Api.Modules.Auth.Entities.User
                {
                    Id = userId,
                    Email = $"{userId}@altomate.com",
                    Name = "Limited Admin",
                    PasswordHash = "x",
                    CreatedAt = DateTime.UtcNow,
                });
            }

            var membership = await db.OrganizationMemberships.IgnoreQueryFilters()
                .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == userId);
            if (membership is null)
            {
                membership = new OrganizationMembership
                {
                    OrganizationId = orgId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                };
                db.OrganizationMemberships.Add(membership);
            }

            membership.Role = "Admin";
            membership.PolicyScope = policyScope;
            membership.Modules = modules;
            membership.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // A staff member on `policyId` with NO EmployeeProfile row — the
        // payroll roster lists them under a blank stand-in profile.
        public async Task AddStaffWithoutAPayrollProfileAsync(string userId, string policyId)
        {
            var orgId = await DemoOrgIdAsync();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new AltomateHR.Api.Modules.Auth.Entities.User
            {
                Id = userId,
                Email = $"{userId}@altomate.com",
                Name = "New Starter",
                PasswordHash = "x",
                CreatedAt = DateTime.UtcNow,
            });
            db.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = orgId,
                UserId = userId,
                Role = "Employee",
                PolicyId = policyId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        public async Task<string> AddPolicyAsync(string name)
        {
            var orgId = await DemoOrgIdAsync();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var policy = new EmployeePolicy
            {
                OrganizationId = orgId,
                Name = name,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            db.EmployeePolicies.Add(policy);
            await db.SaveChangesAsync();
            return policy.Id;
        }

        public async Task PutOnPolicyAsync(string userId, string policyId)
        {
            var orgId = await DemoOrgIdAsync();
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var membership = await db.OrganizationMemberships.IgnoreQueryFilters()
                .FirstAsync(m => m.OrganizationId == orgId && m.UserId == userId);
            membership.PolicyId = policyId;
            await db.SaveChangesAsync();
        }

        public async Task<string> AddOrganizationAsync(string name)
        {
            _ = Client;
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var org = new AltomateHR.Api.Modules.Organizations.Entities.Organization
            {
                Name = name,
                CreatedAt = DateTime.UtcNow,
            };
            db.Organizations.Add(org);
            await db.SaveChangesAsync();
            return org.Id;
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
