using System.Security.Claims;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Auth;

// Thin HTTP layer: reads/writes the cookie, calls IAuthService, shapes the response.
// NO business logic, NO repository access — that all lives in AuthService.
[ApiController]
[Route("[controller]")]        // → /auth
public class AuthController : ControllerBase
{
    // Public so the SSO callback (Partners/InboundSsoController) sets the very
    // cookie /auth/refresh reads — see the note there.
    public const string RefreshCookie = "refreshToken";

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

    // POST /auth/forgot-password — emails a one-time reset code.
    //
    // ALWAYS returns 204, whether or not the address belongs to an account. A
    // 404-vs-204 split here would let anyone test which emails are registered.
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto dto, CancellationToken cancellationToken)
    {
        await _auth.ForgotPasswordAsync(dto.Email.Trim(), cancellationToken);
        return NoContent();
    }

    // POST /auth/reset-password — redeem the code and set a new password.
    // Every failure returns the same message, for the same reason as above.
    [AllowAnonymous]
    [HttpPost("reset-password")]
    [EnableRateLimiting("auth-forgot-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto dto)
    {
        var error = await _auth.ResetPasswordAsync(dto.Email.Trim(), dto.Otp, dto.NewPassword);
        return error is null ? NoContent() : BadRequest(new { message = error });
    }

    // POST /auth/change-password — a signed-in user changing their own password.
    //
    // Authenticated, and the user comes from the token rather than the body, so
    // one account can't change another's. Rate-limited on the same policy as the
    // reset flow: this endpoint also accepts a password guess, so it needs the
    // same protection against being walked.
    //
    // Every session is revoked on success, including this one — so the refresh
    // cookie is cleared here too. Leaving it would hand the client a cookie the
    // server has already thrown away, and the next silent refresh would fail
    // for no visible reason.
    [Authorize]
    [HumanOnly]
    [HttpPost("change-password")]
    [EnableRateLimiting("auth-forgot-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return Unauthorized();

        var error = await _auth.ChangePasswordAsync(userId, dto.CurrentPassword, dto.NewPassword, viaSso: _currentUser.IsSso);
        if (error is not null) return BadRequest(new { message = error });

        Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/auth" });
        return NoContent();
    }

    // POST /auth/switch-org/{organizationId} — re-mint the token for another org you belong to.
    [Authorize]
    [HumanOnly]
    [HttpPost("switch-org/{organizationId}")]
    public async Task<ActionResult<AuthResponseDto>> SwitchOrg(string organizationId)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Unauthorized();

        var result = await _auth.SwitchOrgAsync(userId, organizationId, sso: _currentUser.IsSso);
        if (result is null)
            return Forbid();   // you're not a member of that org

        SetRefreshCookie(result);
        return Ok(ToResponse(result));
    }

    // POST /auth/support/enter/{organizationId} — Fusioneta support: act as an
    // Admin inside ANY org. Superadmins only (SUPERADMIN_EMAILS).
    [Authorize(Policy = AuthPolicies.Superadmin)]
    [HumanOnly]
    [HttpPost("support/enter/{organizationId}")]
    public async Task<ActionResult<AuthResponseDto>> EnterSupport(string organizationId)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Unauthorized();

        var result = await _auth.EnterSupportAsync(userId, organizationId, sso: _currentUser.IsSso);
        if (result is null) return NotFound(new { message = "No such organization." });

        SetRefreshCookie(result);
        return Ok(ToResponse(result));
    }

    // POST /auth/support/exit — back to your own org.
    [Authorize]
    [HumanOnly]
    [HttpPost("support/exit")]
    public async Task<ActionResult<AuthResponseDto>> ExitSupport()
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Unauthorized();

        var result = await _auth.ExitSupportAsync(userId, sso: _currentUser.IsSso);
        if (result is null) return Unauthorized(new { message = "You have no organization of your own to return to." });

        SetRefreshCookie(result);
        return Ok(ToResponse(result));
    }

    // POST /auth/leave-org/{organizationId} — a former employee removes a
    // company they no longer work at from their account. If it was the active
    // one, the session moves to their home company (200 with the new session),
    // or ends when none is left (204, cookie cleared).
    [Authorize]
    [HumanOnly]
    [HttpPost("leave-org/{organizationId}")]
    public async Task<IActionResult> LeaveOrg(string organizationId)
    {
        var userId = _currentUser.UserId;
        if (userId is null) return Unauthorized();

        var result = await _auth.LeaveFormerOrgAsync(
            userId, organizationId, _currentUser.OrganizationId, sso: _currentUser.IsSso);
        if (result.Error is not null) return BadRequest(new { error = result.Error });

        if (result.SignOut)
        {
            if (Request.Cookies.TryGetValue(RefreshCookie, out var cookie)) await _auth.LogoutAsync(cookie);
            Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/auth" });
            return NoContent();
        }

        if (result.Next is null) return Ok(new { switched = false });

        SetRefreshCookie(result.Next);
        return Ok(ToResponse(result.Next));
    }

    // GET /auth/orgs — the orgs this account can switch into.
    [Authorize]
    [HumanOnly]
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
            Name = result.Name,
            Role = result.Role,
            ActiveOrganizationId = result.OrganizationId,
            ActiveOrganizationName = result.OrganizationName,
            IsSuperadmin = result.IsSuperadmin,
            SupportMode = result.SupportMode,
            ViaSso = result.ViaSso,
            FormerEmployee = result.FormerEmployee,
        };
}
