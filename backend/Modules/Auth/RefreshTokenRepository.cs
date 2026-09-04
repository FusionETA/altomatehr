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

    public async Task RevokeAllForUserAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var live = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync();

        if (live.Count == 0) return;

        foreach (var token in live) token.RevokedAt = now;
        await _db.SaveChangesAsync();
    }
}
