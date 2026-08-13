using AltomateHR.Api.Modules.Auth.Entities;

namespace AltomateHR.Api.Modules.Auth;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);
    Task AddAsync(User user);
    Task UpdateAsync(User user);
    Task<bool> AnyAsync();
}
