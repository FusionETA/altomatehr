using AltomateHR.Api.Modules.Auth.Entities;

namespace AltomateHR.Api.Modules.Auth;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token);
    Task<RefreshToken?> GetByTokenAsync(string token);
    Task UpdateAsync(RefreshToken token);

    // Kill every live session for a user. Used after a password reset so an
    // attacker holding a stolen refresh token can't outlive the reset.
    Task RevokeAllForUserAsync(string userId);
}
