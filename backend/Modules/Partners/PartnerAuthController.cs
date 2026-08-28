using AltomateHR.Api.Modules.Partners.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Partners;

// The generic partner token endpoints — one route set for EVERY external app.
// Which app is calling comes from the client secret (Authorization: Bearer), not
// the URL, so adding app #6 is a registry row, never new routes here.
//
// [AllowAnonymous]: these aren't authenticated by JWT or wp_live — the client
// secret in the header IS the credential, verified inside the service.
[ApiController]
[Route("partner")]
[AllowAnonymous]
public class PartnerAuthController : ControllerBase
{
    private readonly IPartnerAuthService _partners;

    public PartnerAuthController(IPartnerAuthService partners) => _partners = partners;

    // POST /partner/token — client secret + single-use ticket → scoped token.
    [HttpPost("token")]
    public async Task<IActionResult> Token(PartnerTokenRequestDto dto)
    {
        var secret = BearerSecret();
        if (secret is null) return Unauthorized(Err("Missing client secret."));

        var result = await _partners.RedeemTicketAsync(secret, dto.Ticket ?? string.Empty);
        return result is null ? Unauthorized(Err("Invalid client secret or ticket.")) : Ok(result);
    }

    // POST /partner/token/refresh — client secret + refresh token → fresh access.
    [HttpPost("token/refresh")]
    public async Task<IActionResult> Refresh(PartnerRefreshRequestDto dto)
    {
        var secret = BearerSecret();
        if (secret is null) return Unauthorized(Err("Missing client secret."));

        var result = await _partners.RefreshAsync(secret, dto.RefreshToken ?? string.Empty);
        return result is null ? Unauthorized(Err("Invalid client secret or refresh token.")) : Ok(result);
    }

    private string? BearerSecret()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header)) return null;
        var value = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : header.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static object Err(string message) => new { error = new { status = 401, message } };
}
