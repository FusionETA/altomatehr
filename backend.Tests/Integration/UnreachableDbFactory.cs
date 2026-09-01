using AltomateHR.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AltomateHR.Api.Tests.Integration;

// The API pointed at a port with nothing listening. Deterministic, and it never
// touches the real container — stopping that would take down whatever else the
// developer is running, and leave it down if a test crashed mid-way.
public sealed class UnreachableDbFactory : WebApplicationFactory<Program>
{
    // Short connect timeout: the point is to prove it fails cleanly, not to make
    // the suite sit through the default 15s per attempt.
    private const string Dead =
        "Server=127.0.0.1;Port=3399;Database=nothing;User=root;Password=x;" +
        "SslMode=None;AllowPublicKeyRetrieval=True;ConnectionTimeout=2";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", Dead);
        builder.UseSetting("Jwt:Key", "integration-test-key-long-enough-for-hmac-sha256-signing");
        builder.UseSetting("Jwt:Issuer", "altomatehr-api");
        builder.UseSetting("Jwt:Audience", "altomatehr-client");
        builder.UseSetting("Jwt:AccessTokenMinutes", "15");
        builder.UseSetting("Jwt:RefreshTokenDays", "7");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseMySql(Dead, new MySqlServerVersion(new Version(8, 0, 0)),
                    my => my.EnableRetryOnFailure(maxRetryCount: 2)));
        });
    }
}
