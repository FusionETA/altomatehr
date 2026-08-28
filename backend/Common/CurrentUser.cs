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

    // The caller's real remote IP, for the attendance IP-allowlist.
    //
    // We sit behind ONE reverse proxy (nginx on the droplet), which appends the
    // real client to X-Forwarded-For (`$proxy_add_x_forwarded_for`). So the LAST
    // hop is the IP nginx actually saw and is trustworthy; the FIRST hops are
    // whatever the client sent and are spoofable — a client could set
    // `X-Forwarded-For: <an-allowed-ip>` to slip past the allowlist. A SECURITY
    // control must therefore never trust the first hop.
    //
    // (Assumes exactly one trusted proxy in front. If that ever changes, move to
    // ForwardedHeaders middleware with KnownProxies instead.)
    public string? IpAddress
    {
        get
        {
            var context = _http.HttpContext;
            if (context is null) return null;

            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrWhiteSpace(forwarded))
            {
                var hops = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (hops.Length > 0) return hops[^1];   // last hop = the proxy-appended real client
            }

            return context.Connection.RemoteIpAddress?.ToString();
        }
    }
}
