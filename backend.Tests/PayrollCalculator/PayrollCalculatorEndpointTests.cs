using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AltomateHR.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AltomateHR.Api.Tests.PayrollCalculator;

// The public endpoint through the real pipeline: what the altomate.io widget
// relies on — snake_case JSON, no login, its CORS origin, the per-IP limit and
// the error shape.
public class PayrollCalculatorEndpointTests
{
    private const string Path = "/payroll-calculator";

    private static readonly object Tc1 = new
    {
        monthly_salary = 5000,
        bonus = 0,
        citizenship = "malaysian_pr",
        age_group = "below_60",
        marital_status = "single",
        children = 0,
        tax_resident = true,
        other_reliefs_yearly = 0,
        zakat_monthly = 0,
    };

    [Fact]
    public async Task Calculate_WithoutLogin_ReturnsTheWidgetsShape()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, Tc1);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        foreach (var key in new[] { "tax_year", "gross", "take_home", "employer_cost", "employee_total", "employer_total", "items" })
            Assert.True(root.TryGetProperty(key, out _), $"missing {key}");

        var first = root.GetProperty("items")[0];
        Assert.Equal("epf", first.GetProperty("code").GetString());
        Assert.Equal("KWSP", first.GetProperty("sub_label").GetString());
        Assert.Equal(JsonValueKind.Number, first.GetProperty("employee").ValueKind);
        Assert.Equal(JsonValueKind.Array, first.GetProperty("working").ValueKind);
    }

    [Fact]
    public async Task InvalidField_Returns400WithTheField()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(Path, new { monthly_salary = 0 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var error = json.RootElement.GetProperty("error");
        Assert.Equal("monthly_salary", error.GetProperty("field").GetString());
        Assert.Equal("out_of_range", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task BrokenJson_Returns400WithoutAField()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(Path, new StringContent("{ not json", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var error = json.RootElement.GetProperty("error");
        Assert.False(error.TryGetProperty("field", out _));
        Assert.Equal("invalid_type", error.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("https://altomate.io", true)]
    [InlineData("https://www.altomate.io", true)]
    [InlineData("https://evil.example", false)]
    public async Task Preflight_AllowsOnlyTheMarketingSite(string origin, bool allowed)
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, Path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type");
        var response = await client.SendAsync(request);

        var header = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
        Assert.Equal(allowed ? origin : null, header);
        // No cookies on a public calculator.
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }

    [Fact]
    public async Task ThirtyFirstCallInAMinute_Is429WithRetryAfter()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 31; i++) last = await client.PostAsJsonAsync(Path, Tc1);

        Assert.Equal(HttpStatusCode.TooManyRequests, last!.StatusCode);
        Assert.True(last.Headers.Contains("Retry-After"));
        using var json = await JsonDocument.ParseAsync(await last.Content.ReadAsStreamAsync());
        Assert.Equal("rate_limited", json.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private sealed class TestApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _databaseName = $"altomatehr-tests-{Guid.NewGuid()}";

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
                    ["Seed:DemoData"] = "false",
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
