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

    public AuthController(IAuthService auth) => _auth = auth;

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
        new() { Token = result.AccessToken, Email = result.Email, Role = result.Role };
}
