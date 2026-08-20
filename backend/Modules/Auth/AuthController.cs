using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AltomateHR.Api.Modules.Auth;

// Thin HTTP layer: reads/writes the cookie, calls IAuthService, shapes the response.
// NO business logic, NO repository access — that all lives in AuthService.
[ApiController]
[Route("[controller]")]        // → /auth
public class AuthController : ControllerBase
{
    private const string RefreshCookie = "refreshToken";

    private readonly IAuthService _auth;
    private readonly ICurrentUser _currentUser;

    public AuthController(IAuthService auth, ICurrentUser currentUser)
    {
        _auth = auth;
        _currentUser = currentUser;
    }

    // POST /auth/login
    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var result = await _auth.LoginAsync(dto.Email, dto.Password);
        if (result is null)
            return Unauthorized(new { message = "Invalid credentials." });

        SetRefreshCookie(result);
        return Ok(ToResponse(result));
    }

    // POST /auth/refresh
    [AllowAnonymous]
    [HttpPost("refresh")]
    [EnableRateLimiting("auth-refresh")]
    public async Task<ActionResult<AuthResponseDto>> Refresh()
    {
        var cookie = Request.Cookies[RefreshCookie];
        if (cookie is null) return Unauthorized(new { message = "Unable to refresh session." });

        var result = await _auth.RefreshAsync(cookie);
        if (result is null)
            return Unauthorized(new { message = "Unable to refresh session." });

        SetRefreshCookie(result);
        return Ok(ToResponse(result));
    }

    // POST /auth/logout
    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var cookie = Request.Cookies[RefreshCookie];
        if (cookie is not null)
            await _auth.LogoutAsync(cookie);

        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/auth" });
        return NoContent();
    }

    // POST /auth/switch-org/{organizationId} — re-mint the token for another org you belong to.
    [Authorize]
    [HttpPost("switch-org/{organizationId}")]
    public async Task<ActionResult<AuthResponseDto>> SwitchOrg(string organizationId)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Unauthorized();

        var result = await _auth.SwitchOrgAsync(userId, organizationId);
        if (result is null)
            return Forbid();   // you're not a member of that org

        SetRefreshCookie(result);
        return Ok(ToResponse(result));
    }

    // GET /auth/orgs — the orgs this account can switch into.
    [Authorize]
    [HttpGet("orgs")]
    public async Task<ActionResult<IReadOnlyList<UserOrgDto>>> Orgs()
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Unauthorized();
        return Ok(await _auth.GetOrgsAsync(userId));
    }

    // ---- HTTP concerns only (cookies live in the controller — they need Request/Response) ----

    private void SetRefreshCookie(AuthResult result)
    {
        Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,               // JavaScript CANNOT read it → XSS can't steal it
            Secure = !HttpContext.RequestServices
                .GetRequiredService<IHostEnvironment>()
                .IsDevelopment(),          // true in production HTTPS; false for local http dev
            SameSite = SameSiteMode.Lax,
            Path = "/auth",                // only sent to /auth/* endpoints
            Expires = result.RefreshTokenExpiresAt,
        });
    }

    private static AuthResponseDto ToResponse(AuthResult result) =>
        new()
        {
            Token = result.AccessToken,
            Email = result.Email,
            Role = result.Role,
            ActiveOrganizationId = result.OrganizationId,
        };
}
