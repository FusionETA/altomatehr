using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AltomateHR.Api.Tests.Integration;

// Endpoint-level tests against a REAL MySQL: the full pipeline — routing, model
// binding, auth, EF Core, and actual SQL. The unit suite proves the rules; this
// proves the wiring, and catches what only surfaces in the database: a
// migration that won't apply, a column type that rejects a value, a query the
// in-memory provider translates differently.
//
// Skipped, not failed, when no MySQL is reachable, so the unit suite still runs
// green for a developer without the container up.
[Collection("mysql")]
public class LeaveApiIntegrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task FullLeaveLifecycle_AgainstRealMySql()
    {
        Skip.IfNot(await MySqlApiFactory.IsAvailableAsync(), "MySQL is not reachable on port 3307.");

        await using var api = new MySqlApiFactory();
        await api.InitializeAsync();
        using var client = api.CreateClient();

        // ── sign in as the seeded Owner ─────────────────────────────
        var admin = await LoginAsync(client, "admin@altomate.com");
        var supervisor = await LoginAsync(client, "supervisor@altomate.com");
        var employee = await LoginAsync(client, "employee@altomate.com");

        // ── the seed ran against real SQL ───────────────────────────
        Use(client, admin);
        var types = await GetJsonAsync(client, "/leave-types");
        Assert.NotEmpty(types.EnumerateArray());

        var annual = types.EnumerateArray().First(t => t.GetProperty("code").GetString() == "AL");
        var annualId = annual.GetProperty("id").GetString()!;

        // ── apply ───────────────────────────────────────────────────
        Use(client, employee);
        var applied = await PostJsonAsync(client, "/leave", new
        {
            leaveTypeId = annualId,
            startDate = "2031-09-01",
            endDate = "2031-09-02",
        });
        Assert.Equal("PENDING", applied.GetProperty("status").GetString());
        var applicationId = applied.GetProperty("id").GetString()!;

        // Submitting must NOT move the balance — pending is reported separately.
        var before = await BalanceAsync(client, annualId, 2031);
        Assert.Equal(0, before.Taken);
        Assert.True(before.Pending > 0);

        // ── approve, as the supervisor who actually owns the step ───
        Use(client, supervisor);
        var approve = await client.PostAsync($"/leave/{applicationId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

        // ── now it is deducted ──────────────────────────────────────
        Use(client, employee);
        var after = await BalanceAsync(client, annualId, 2031);
        Assert.True(after.Taken > 0);
        Assert.Equal(before.Remaining - after.Taken, after.Remaining);
    }

    [SkippableFact]
    public async Task AccessRules_HoldOverRealHttp()
    {
        Skip.IfNot(await MySqlApiFactory.IsAvailableAsync(), "MySQL is not reachable on port 3307.");

        await using var api = new MySqlApiFactory();
        await api.InitializeAsync();
        using var client = api.CreateClient();

        var employee = await LoginAsync(client, "employee@altomate.com");

        // No token at all.
        client.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/leave/balances")).StatusCode);

        // Authenticated, but not entitled to the admin surfaces.
        Use(client, employee);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/leave/balances/all?year=2031")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/leave/team")).StatusCode);

        // Range validation runs before anything touches the database.
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/leave/balances?year=1999")).StatusCode);
    }

    [SkippableFact]
    public async Task Migrations_ApplyCleanly_AndSeedRealRows()
    {
        Skip.IfNot(await MySqlApiFactory.IsAvailableAsync(), "MySQL is not reachable on port 3307.");

        // InitializeAsync runs the real migration set. If any migration were
        // broken — a duplicate column, a bad default, a bad index — this throws
        // here rather than surfacing in production.
        await using var api = new MySqlApiFactory();
        await api.InitializeAsync();
        using var client = api.CreateClient();

        var admin = await LoginAsync(client, "admin@altomate.com");
        Use(client, admin);

        var employees = await GetJsonAsync(client, "/employees");
        Assert.NotEmpty(employees.EnumerateArray());
    }

    // ── helpers ─────────────────────────────────────────────────────
    private static void Use(HttpClient c, string token) =>
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<string> LoginAsync(HttpClient client, string email)
    {
        var res = await client.PostAsJsonAsync("/auth/login", new { email, password = "password123" });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("token").GetString()!;
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url)
    {
        var res = await client.GetAsync(url);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static async Task<JsonElement> PostJsonAsync(HttpClient client, string url, object body)
    {
        var res = await client.PostAsJsonAsync(url, body);
        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static async Task<(double Taken, double Pending, double Remaining)> BalanceAsync(
        HttpClient client, string leaveTypeId, int year)
    {
        var balances = await GetJsonAsync(client, $"/leave/balances?year={year}");
        var row = balances.EnumerateArray()
            .First(b => b.GetProperty("leaveTypeId").GetString() == leaveTypeId);
        return (row.GetProperty("takenDays").GetDouble(),
                row.GetProperty("pendingDays").GetDouble(),
                row.GetProperty("remainingDays").GetDouble());
    }
}

// Serialised: each test builds and drops its own schema, and running them in
// parallel against one MySQL makes failures hard to read.
[CollectionDefinition("mysql", DisableParallelization = true)]
public class MySqlCollection { }
