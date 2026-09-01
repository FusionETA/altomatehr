using AltomateHR.Api.Modules.Auth.Entities;

namespace AltomateHR.Api.Modules.Auth;

public interface IPasswordResetOtpRepository
{
    Task AddAsync(PasswordResetOtp otp);

    // The newest still-redeemable code for this email, or null. Ordering matters:
    // requesting a second code invalidates the first, but we still want the latest.
    Task<PasswordResetOtp?> GetActiveByEmailAsync(string email);

    Task UpdateAsync(PasswordResetOtp otp);

    // Called before issuing a new code so only one is ever live per user.
    Task InvalidateAllForUserAsync(string userId);
}
