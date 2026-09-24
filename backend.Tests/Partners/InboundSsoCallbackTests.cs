using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Email;
using AltomateHR.Api.Modules.Partners;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Tests.Partners;

// The callback set its session cookie as "altomate_refresh" while login — and so
// /auth/refresh — used "refreshToken". Every SSO hand-off from New-Altomate then
// landed on the sign-in page, looking exactly like a dead ticket.
public class InboundSsoCallbackTests
{
    private sealed class OneTicket : IInboundSsoService
    {
        public Task<InboundTicket?> MintAsync(string email, string organizationId) => throw new NotSupportedException();
        public Task<AuthResult?> RedeemAsync(string ticket) => Task.FromResult<AuthResult?>(ticket == "tkt_good"
            ? new AuthResult("access", "owner@acme.com", "Owner", "org-1", "refresh-123", DateTime.UtcNow.AddDays(7))
            : null);
    }

    private static (InboundSsoController Controller, HttpContext Http) Make()
    {
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = Environments.Production })
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var controller = new InboundSsoController(
            new OneTicket(), Options.Create(new PortalOptions { BaseUrl = "https://hr.example" }))
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
        return (controller, http);
    }

    [Fact]
    public async Task ARedeemedTicketSetsTheCookieRefreshReads()
    {
        var (controller, http) = Make();

        var result = await controller.Callback("tkt_good");

        Assert.Equal("https://hr.example", Assert.IsType<RedirectResult>(result).Url);
        var cookie = http.Response.Headers.SetCookie.ToString();
        Assert.StartsWith($"{AuthController.RefreshCookie}=refresh-123", cookie);
        Assert.Contains("path=/auth", cookie);
        Assert.Contains("httponly", cookie);
    }

    [Fact]
    public async Task ADeadTicketSetsNothingAndSaysSo()
    {
        var (controller, http) = Make();

        var result = await controller.Callback("tkt_spent");

        Assert.Equal("https://hr.example/?sso=expired", Assert.IsType<RedirectResult>(result).Url);
        Assert.Empty(http.Response.Headers.SetCookie.ToString());
    }
}
