using AltomateHR.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MySqlConnector;

namespace AltomateHR.Api.Tests.Integration;

// Boots the real API against a REAL MySQL, not the in-memory provider the auth
// endpoint tests use. That provider fakes the database entirely, so it can't
// catch anything that only shows up in SQL: a missing index, a bad column type,
// a query the provider translates differently, or a migration that won't apply.
//
// Each run gets its own schema (altomatehr_it_<guid>) built by running the real
// migrations, then drops it. Nothing is shared between tests, and the developer
// database is never touched.
public sealed class MySqlApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The container from `docker run --name altomatehr-mysql -p 3307:3306`.
    private const string Host = "Server=127.0.0.1;Port=3307;User=root;Password=devroot;" +
                                "SslMode=None;AllowPublicKeyRetrieval=True";

    // Kept SHORT deliberately. EF takes a MySQL user-level lock named
    // "__<database>_EFMigrationsLock", and MySQL rejects lock names over 64
    // characters — a full guid suffix tips it over by one.
    private readonly string _database = $"it_{Guid.NewGuid():N}"[..19];

    public string ConnectionString => $"{Host};Database={_database}";

    // Skips the suite rather than failing it when no MySQL is around — a
    // developer without the container running still gets a green unit run.
    public static async Task<bool> IsAvailableAsync()
    {
        try
        {
            await using var conn = new MySqlConnection($"{Host};Database=mysql");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await conn.OpenAsync(cts.Token);
            return true;
        }
        catch { return false; }
    }

    public async Task InitializeAsync()
    {
        await using var conn = new MySqlConnection($"{Host};Database=mysql");
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE IF NOT EXISTS `{_database}`";
        await cmd.ExecuteNonQueryAsync();

        // Apply the real migrations — this alone catches a migration that
        // doesn't apply cleanly, which no unit test can.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public new async Task DisposeAsync()
    {
        try
        {
            await using var conn = new MySqlConnection($"{Host};Database=mysql");
            await conn.OpenAsync();
            var cmd = conn.CreateCommand();
            cmd.CommandText = $"DROP DATABASE IF EXISTS `{_database}`";
            await cmd.ExecuteNonQueryAsync();
        }
        catch { /* best effort — a leftover test schema is harmless */ }
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // UseSetting, NOT ConfigureAppConfiguration.
        //
        // Program.cs reads Jwt:Key and the connection string EAGERLY, as top-level
        // statements, to build the token-validation parameters. ConfigureAppConfiguration
        // callbacks are applied after that point, so values set there arrive too late for
        // startup: validation would keep the developer's user-secret key while TokenService
        // signed with the test one, and every authenticated request came back
        // 401 "The signature key was not found".
        //
        // UseSetting writes into the host builder's configuration before Program.cs runs,
        // so both halves see the same values.
        foreach (var (key, value) in new Dictionary<string, string>
                 {
                     ["ConnectionStrings:Default"] = ConnectionString,
                     ["Jwt:Key"] = "integration-test-key-long-enough-for-hmac-sha256-signing",
                     ["Jwt:Issuer"] = "altomatehr-api",
                     ["Jwt:Audience"] = "altomatehr-client",
                     ["Jwt:AccessTokenMinutes"] = "15",
                     ["Jwt:RefreshTokenDays"] = "7",
                 })
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseMySql(ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)),
                    my => my.EnableRetryOnFailure(maxRetryCount: 5)));
        });
    }
}
