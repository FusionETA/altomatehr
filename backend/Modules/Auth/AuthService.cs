using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Modules.Auth;

// Orchestration: validate credentials (against hashed passwords), resolve which org
// the account is acting in (via its memberships), mint tokens, store/rotate/revoke
// refresh tokens. The token carries the ACTIVE org + the role for that org.
public class AuthService : IAuthService
{
    private readonly ITokenService _tokens;
    private readonly IRefreshTokenRepository _refreshRepo;
    private readonly IUserRepository _userRepo;
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly int _refreshDays;

    public AuthService(
        ITokenService tokens,
        IRefreshTokenRepository refreshRepo,
        IUserRepository userRepo,
        IOrganizationMembershipRepository memberships,
        IConfiguration config)
    {
        _tokens = tokens;
        _refreshRepo = refreshRepo;
        _userRepo = userRepo;
        _memberships = memberships;
        _refreshDays = int.Parse(config["Jwt:RefreshTokenDays"] ?? "7");
    }

    public async Task<AuthResult?> LoginAsync(string email, string password)
    {
        var user = await _userRepo.GetByEmailAsync(email);

        // Verify against the stored hash — BCrypt (native) OR legacy scrypt (migrated
        // from the monolith). Exception-safe: a bad hash fails the login, never 500s.
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
            return null;

        // Transparent upgrade: a legacy scrypt password that just verified is re-hashed
        // to BCrypt, so this account's NEXT login uses the native format and the old
        // hash quietly ages out.
        if (PasswordHasher.IsLegacyScrypt(user.PasswordHash))
        {
            user.PasswordHash = PasswordHasher.HashBcrypt(password);
            await _userRepo.UpdateAsync(user);
        }

        // Log the account into its default (first) org. Role comes from that membership.
        var memberships = await _memberships.GetByUserAsync(user.Id);
        var active = memberships.FirstOrDefault();
        if (active is null)
            return null;   // valid credentials, but not a member of any org yet

        return await IssueTokensAsync(user.Id, user.Email, active.Role, active.OrganizationId);
    }

    public async Task<AuthResult?> SwitchOrgAsync(string userId, string organizationId)
    {
        // Only if the account is actually a member of the target org.
        var membership = await _memberships.GetAsync(organizationId, userId);
        if (membership is null) return null;

        var user = await _userRepo.GetByIdAsync(userId);
        if (user is null) return null;

        return await IssueTokensAsync(userId, user.Email, membership.Role, organizationId);
    }

    public async Task<IReadOnlyList<UserOrgDto>> GetOrgsAsync(string userId) =>
        (await _memberships.GetByUserAsync(userId))
            .Select(m => new UserOrgDto(m.OrganizationId, m.Role))
            .ToList();

    public async Task<AuthResult?> RefreshAsync(string refreshToken)
    {
        var stored = await _refreshRepo.GetByTokenAsync(refreshToken);
        if (stored is null || !stored.IsActive)
            return null;

        // Rotate: revoke the used token, then issue a fresh pair for the SAME active org.
        stored.RevokedAt = DateTime.UtcNow;
        await _refreshRepo.UpdateAsync(stored);

        return await IssueTokensAsync(stored.UserId, stored.Email, stored.Role, stored.OrganizationId);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var stored = await _refreshRepo.GetByTokenAsync(refreshToken);
        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = DateTime.UtcNow;
            await _refreshRepo.UpdateAsync(stored);
        }
    }

    private async Task<AuthResult> IssueTokensAsync(string userId, string email, string role, string organizationId)
    {
        var accessToken = _tokens.CreateToken(userId, email, role, organizationId);

        var refresh = new RefreshToken
        {
            Token = _tokens.CreateRefreshToken(),
            UserId = userId,
            Email = email,
            Role = role,
            OrganizationId = organizationId,
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshDays),
            CreatedAt = DateTime.UtcNow,
        };
        await _refreshRepo.AddAsync(refresh);

        return new AuthResult(accessToken, email, role, organizationId, refresh.Token, refresh.ExpiresAt);
    }
}
