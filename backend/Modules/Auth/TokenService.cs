using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;   // "Claim" here = an IDENTITY claim, not our expense-Claim entity
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AltomateHR.Api.Modules.Auth;

public class TokenService : ITokenService
{
    public const string SupportClaim = "support";
    public const string SsoClaim = "sso";
    public const string FormerClaim = "former";

    // The activity session (Modules/Activity) — read back by ICurrentUser.SessionId.
    // Not the registered "sid": the JWT handler's default inbound mapping renames it.
    public const string SessionClaim = "asid";

    private readonly IConfiguration _config;

    public TokenService(IConfiguration config) => _config = config;

    // The short-lived ACCESS token (a signed JWT).
    public string CreateToken(string userId, string email, string role, string organizationId) =>
        CreateToken(userId, email, role, organizationId, support: false);

    public string CreateToken(
        string userId, string email, string role, string organizationId, bool support, bool sso = false, bool former = false) =>
        CreateToken(userId, email, role, organizationId, support, sso, former, sessionId: null);

    public string CreateToken(
        string userId, string email, string role, string organizationId, bool support, bool sso, bool former, string? sessionId)
    {
        var jwt = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.Role, role),
            new Claim("org", organizationId),   // the tenant — read back by ICurrentUser + the query filter
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        // A Fusioneta staff member acting inside a customer's org. ICurrentUser
        // reads it; the audit log masks the actor on it.
        if (support) claims.Add(new Claim(SupportClaim, "1"));

        // Signed in through the Altomate SSO hand-off. Carried in the token so
        // switching company keeps it; the shells hide New company, Change
        // password and Log out on it — the account is managed in Altomate.
        if (sso) claims.Add(new Claim(SsoClaim, "1"));

        // A company this person used to work at (archived there — left, or
        // transferred out). Kept so they can still read their payslips; the
        // FormerEmployeeReadOnlyMiddleware refuses every write on it.
        if (former) claims.Add(new Claim(FormerClaim, "1"));

        // Which sign-in this token belongs to, so what the person does can be
        // tied back to it. Carried across refreshes on the RefreshToken.
        if (!string.IsNullOrEmpty(sessionId)) claims.Add(new Claim(SessionClaim, sessionId));

        var token = new JwtSecurityToken(
            issuer: jwt["Issuer"],
            audience: jwt["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(int.Parse(jwt["AccessTokenMinutes"] ?? "15")),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // The long-lived REFRESH token: an opaque, cryptographically-random string (NOT a JWT).
    public string CreateRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
