using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Auth;

public class PasswordResetOtpRepository : IPasswordResetOtpRepository
{
    private readonly AppDbContext _db;

    public PasswordResetOtpRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(PasswordResetOtp otp)
    {
        _db.PasswordResetOtps.Add(otp);
        await _db.SaveChangesAsync();
    }

    // Expiry and the attempt cap are filtered in SQL; IsActive is [NotMapped]
    // so it can't be used in the query itself.
    public Task<PasswordResetOtp?> GetActiveByEmailAsync(string email) =>
        _db.PasswordResetOtps
            .Where(o => o.Email == email
                        && o.ConsumedAt == null
                        && o.AttemptCount < PasswordResetOtp.MaxAttempts
                        && o.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync();

    public async Task UpdateAsync(PasswordResetOtp otp)
    {
        _db.PasswordResetOtps.Update(otp);
        await _db.SaveChangesAsync();
    }

    // Marks outstanding codes consumed rather than deleting them, so the trail
    // of reset attempts survives — same reasoning as RefreshToken.RevokedAt.
    public async Task InvalidateAllForUserAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var live = await _db.PasswordResetOtps
            .Where(o => o.UserId == userId && o.ConsumedAt == null)
            .ToListAsync();

        if (live.Count == 0) return;

        foreach (var otp in live) otp.ConsumedAt = now;
        await _db.SaveChangesAsync();
    }
}
