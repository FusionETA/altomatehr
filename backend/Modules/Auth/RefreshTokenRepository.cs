using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Auth;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _db;

    public RefreshTokenRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(RefreshToken token)
    {
        _db.RefreshTokens.Add(token);
        await _db.SaveChangesAsync();
    }

    public Task<RefreshToken?> GetByTokenAsync(string token) =>
        _db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == token);

    public async Task UpdateAsync(RefreshToken token)
    {
        _db.RefreshTokens.Update(token);
        await _db.SaveChangesAsync();
    }
}
