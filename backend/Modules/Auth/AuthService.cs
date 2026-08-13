using AltomateHR.Api.Modules.Auth.Entities;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Modules.Auth;

// Orchestration: validate credentials (against hashed passwords in the DB), mint tokens,
// store/rotate/revoke refresh tokens. The controller never calls the repos directly.
public class AuthService : IAuthService
{
    private readonly ITokenService _tokens;
    private readonly IRefreshTokenRepository _refreshRepo;
    private readonly IUserRepository _userRepo;
    private readonly int _refreshDays;

    public AuthService(
        ITokenService tokens,
        IRefreshTokenRepository refreshRepo,
        IUserRepository userRepo,
        IConfiguration config)
    {
        _tokens = tokens;
        _refreshRepo = refreshRepo;
        _userRepo = userRepo;
        _refreshDays = int.Parse(config["Jwt:RefreshTokenDays"] ?? "7");
    }

    public async Task<AuthResult?> LoginAsync(string email, string password)
    {
        var user = await _userRepo.GetByEmailAsync(email);

        // BC.Verify re-hashes `password` with the salt embedded in user.PasswordHash and compares.
        // (We never decrypt — hashing is one-way.)
        if (user is null || !BC.Verify(password, user.PasswordHash))
            return null;

        return await IssueTokensAsync(user.Id, user.Email, user.Role, user.OrganizationId);
    }

    public async Task<AuthResult?> RefreshAsync(string refreshToken)
    {
        var stored = await _refreshRepo.GetByTokenAsync(refreshToken);
        if (stored is null || !stored.IsActive)
            return null;

        // Rotate: revoke the used token, then issue a fresh pair.
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

        return new AuthResult(accessToken, email, role, refresh.Token, refresh.ExpiresAt);
    }
}
