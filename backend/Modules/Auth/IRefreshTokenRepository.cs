using AltomateHR.Api.Modules.Auth.Entities;

namespace AltomateHR.Api.Modules.Auth;

public interface IRefreshTokenRepository
{
    Task AddAsync(RefreshToken token);
    Task<RefreshToken?> GetByTokenAsync(string token);
    Task UpdateAsync(RefreshToken token);
}
