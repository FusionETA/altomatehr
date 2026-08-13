using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AltomateHR.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.TestHost;

namespace AltomateHR.Api.Tests.Auth;

public class AuthEndpointTests
{
    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenAndRefreshCookie()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "admin@altomate.com",
            password = "password123",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies, cookie => cookie.StartsWith("refreshToken=", StringComparison.Ordinal));

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("token").GetString()));
        Assert.Equal("admin@altomate.com", json.RootElement.GetProperty("email").GetString());
        Assert.Equal("Admin", json.RootElement.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "admin@altomate.com",
            password = "wrong-password",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_AfterTooManyAttempts_ReturnsTooManyRequests()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        HttpResponseMessage? response = null;
        for (var i = 0; i < 6; i++)
        {
            response = await client.PostAsJsonAsync("/auth/login", new
            {
                email = "admin@altomate.com",
                password = "wrong-password",
            });
        }

        Assert.NotNull(response);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutCookie_ReturnsUnauthorized()
    {
        using var factory = new TestApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/auth/refresh", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName));
            });
        }
    }
}
