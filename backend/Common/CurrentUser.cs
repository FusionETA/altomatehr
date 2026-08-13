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
}
