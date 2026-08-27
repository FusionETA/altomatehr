using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AltomateHR.Api.Common;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUser(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? Principal => _http.HttpContext?.User;

    public string? UserId =>
        Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);

    public string? OrganizationId => Principal?.FindFirstValue("org");

    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);

    public bool IsAdmin => Principal?.IsInRole("Admin") ?? false;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    // Prefer X-Forwarded-For's first hop when present — behind Caddy (see the
    // deploy compose file) RemoteIpAddress is the proxy, not the real client.
    public string? IpAddress
    {
        get
        {
            var context = _http.HttpContext;
            if (context is null) return null;

            var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var first = forwarded.Split(',')[0].Trim();
                if (first.Length > 0) return first;
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }
}
