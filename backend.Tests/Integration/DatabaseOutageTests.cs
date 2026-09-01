using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MySqlConnector;

namespace AltomateHR.Api.Tests.Integration;

// What the API does when the database is unreachable — the case that only shows
// up in production, at the worst moment.
//
// Startup and runtime are DIFFERENT failures and are tested separately:
//
//   • Startup — Program.cs applies migrations before serving, so an unreachable
//     database means the app does not start at all. It cannot return an error
//     because there is no host. That is a deliberate crash-fast: an orchestrator
//     restarts it, and a half-migrated app serving traffic would be worse.
//
//   • Runtime — the database goes away AFTER the app is up. Here it must fail
//     cleanly per request and recover on its own once the database returns,
//     without a restart.
[Collection("mysql")]
public class DatabaseOutageTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SkippableFact]
    public async Task Startup_WithUnreachableDatabase_FailsFastRatherThanServingBrokenTraffic()
    {
        Skip.IfNot(await MySqlApiFactory.IsAvailableAsync(), "MySQL is not reachable on port 3307.");

        // Port 3399 has nothing listening — deterministic, and it never touches
        // the shared container, which stopping would take down other work.
        await using var api = new UnreachableDbFactory();

        // Creating the client boots the host, which runs MigrateAsync.
        var boot = await Record.ExceptionAsync(async () =>
        {
            using var client = api.CreateClient();
            await client.GetAsync("/health");
        });

        Assert.NotNull(boot);

        // The failure names the database, so an operator reading logs at 3am can
        // tell a dead dependency from a bug in the app.
        var chain = Flatten(boot!);
        Assert.Contains(chain, e => e is MySqlException || e.Message.Contains("MySQL"));
    }

    [SkippableFact]
    public async Task Runtime_DatabaseDropsMidFlight_Returns500WithoutLeakingInternals()
    {
        Skip.IfNot(await MySqlApiFactory.IsAvailableAsync(), "MySQL is not reachable on port 3307.");

        await using var api = new MySqlApiFactory();
        await api.InitializeAsync();
        using var client = api.CreateClient();

        // Healthy first — proves the 500 below is the outage, not a broken test.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client)).StatusCode);

        // Now pull the schema out from under the running app.
        await DropSchemaAsync(api.ConnectionString);

        var started = DateTime.UtcNow;
        var response = await LoginAsync(client);
        var elapsed = DateTime.UtcNow - started;

        // It fails — but as a controlled 500, not a hang and not a crash.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // EF retries; that must still resolve rather than pinning a request
        // thread for minutes while a caller waits.
        Assert.True(elapsed < TimeSpan.FromSeconds(90),
            $"Took {elapsed.TotalSeconds:0}s to give up — too long to hold a request open.");

        // The body is the JSON envelope we control.
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Something went wrong", body);

        // And the process is still up: one dead dependency must not end the app.
        Assert.Equal(HttpStatusCode.InternalServerError, (await LoginAsync(client)).StatusCode);
    }

    [SkippableFact]
    public async Task Runtime_RecoversOnItsOwn_AfterConnectionsAreKilled()
    {
        Skip.IfNot(await MySqlApiFactory.IsAvailableAsync(), "MySQL is not reachable on port 3307.");

        await using var api = new MySqlApiFactory();
        await api.InitializeAsync();
        using var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(client)).StatusCode);

        // Kill every pooled connection this app holds, without stopping the
        // server — the closest safe analogue of a network blip or a failover.
        await KillConnectionsAsync(api.ConnectionString);

        // The next request must succeed anyway: the pool discards the dead
        // connections and opens fresh ones. No restart, no intervention.
        var after = await LoginAsync(client);
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);

        var body = await after.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("token").GetString()));
    }

    // ── helpers ─────────────────────────────────────────────────────
    private static Task<HttpResponseMessage> LoginAsync(HttpClient client) =>
        client.PostAsJsonAsync("/auth/login",
            new { email = "admin@altomate.com", password = "password123" });

    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!) yield return e;
    }

    private static async Task DropSchemaAsync(string connectionString)
    {
        var b = new MySqlConnectionStringBuilder(connectionString);
        var database = b.Database;
        b.Database = "mysql";

        await using var conn = new MySqlConnection(b.ConnectionString);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS `{database}`";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task KillConnectionsAsync(string connectionString)
    {
        var b = new MySqlConnectionStringBuilder(connectionString);
        var database = b.Database;
        b.Database = "mysql";

        await using var conn = new MySqlConnection(b.ConnectionString);
        await conn.OpenAsync();

        var ids = new List<long>();
        var read = conn.CreateCommand();
        read.CommandText =
            "SELECT id FROM information_schema.PROCESSLIST WHERE db = @db AND id <> CONNECTION_ID()";
        read.Parameters.AddWithValue("@db", database);
        await using (var reader = await read.ExecuteReaderAsync())
            while (await reader.ReadAsync()) ids.Add(reader.GetInt64(0));

        foreach (var id in ids)
        {
            try
            {
                var kill = conn.CreateCommand();
                kill.CommandText = $"KILL {id}";
                await kill.ExecuteNonQueryAsync();
            }
            catch (MySqlException) { /* already gone */ }
        }
    }
}
