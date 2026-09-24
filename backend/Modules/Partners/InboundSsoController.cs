using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Partners;

// SSO coming IN: an external platform dropping one of its provisioned admins
// into AltomateHR already signed in.
//
// Two endpoints, and they are trusted very differently. The mint is
// server-to-server behind a wp_live_ key; the callback is a bare browser
// navigation with nothing but a ticket, which is why the ticket is opaque,
// single-use, and dead in two minutes.
[ApiController]
[Route("sso")]
public class InboundSsoController : ControllerBase
{
    // Must match AuthController's, or the session it sets would not be the one
    // /auth/refresh reads.
    private const string RefreshCookie = "altomate_refresh";

    private readonly IInboundSsoService _sso;
    private readonly PortalOptions _portal;

    public InboundSsoController(
        IInboundSsoService sso, Microsoft.Extensions.Options.IOptions<PortalOptions> portal)
    {
        _sso = sso;
        _portal = portal.Value;
    }

    // POST /sso/ticket — the external platform asks for a hand-off.
    //
    // WHO comes from the body; WHERE comes from the calling key's own org, never
    // from the request. A key can therefore only ever open a door into its own
    // tenant, whatever email it names.
    [Authorize]
    [RequireScope("sso:write")]
    [HttpPost("ticket")]
    public async Task<IActionResult> Mint(MintSsoTicketDto dto)
    {
        var organizationId = User.FindFirst("org")?.Value;
        if (string.IsNullOrEmpty(organizationId))
            return Unauthorized(new { message = "This credential is not scoped to an organization." });

        var ticket = await _sso.MintAsync(dto.Email ?? string.Empty, organizationId);

        // One answer for "no such person", "not in your org" and "not an admin".
        // Telling them apart would turn this into a directory oracle for anyone
        // holding a key.
        return ticket is null
            ? NotFound(new { message = "No admin or owner with that email in this organization." })
            : Ok(new
            {
                ticket = ticket.Ticket,
                redirectPath = ticket.RedirectPath,
                expiresIn = ticket.ExpiresIn,
            });
    }

    // GET /sso/callback?t=... — the customer's browser lands here.
    //
    // AllowAnonymous because a top-level navigation carries no Authorization
    // header; the ticket IS the credential. On success it sets the same
    // httpOnly refresh cookie a login sets and redirects into the app, where
    // the SPA's usual /auth/refresh completes the sign-in. Nothing sensitive
    // rides in the url.
    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery(Name = "t")] string? t)
    {
        var result = string.IsNullOrWhiteSpace(t) ? null : await _sso.RedeemAsync(t);

        // A spent, expired or forged ticket lands on the ordinary sign-in page
        // rather than an error screen. From here it is indistinguishable from
        // arriving logged out, which is what it is.
        if (result is null) return Redirect(Home("sso=expired"));

        Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = !HttpContext.RequestServices
                .GetRequiredService<IHostEnvironment>().IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/auth",
            Expires = result.RefreshTokenExpiresAt,
        });

        return Redirect(Home());
    }

    // The app's own address, from configuration — never from the request. A
    // redirect target a caller could influence is an open redirect with a
    // freshly-minted session attached to it.
    private string Home(string? query = null)
    {
        // Portal:BaseUrl — the same setting the welcome email's button uses.
        var home = (_portal.BaseUrl ?? string.Empty).TrimEnd('/');
        if (home.Length == 0) home = "/";
        return query is null ? home : $"{home}/?{query}";
    }
}

public class MintSsoTicketDto
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.EmailAddress]
    public string? Email { get; set; }
}
