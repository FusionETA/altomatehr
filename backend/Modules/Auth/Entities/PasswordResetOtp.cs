using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltomateHR.Api.Modules.Auth.Entities;

// A one-time code for password reset. Modelled on RefreshToken (the established
// opaque-token pattern here) with three deliberate differences, all because this
// credential grants full account takeover:
//
//   1. HASHED, not plaintext. RefreshToken stores its secret in the clear; a DB
//      leak there costs a session, whereas a leaked reset code costs the account.
//   2. Minutes, not days.
//   3. Attempt-capped — a 6-digit code is only a million guesses, so without a
//      cap an attacker with the email address could brute-force it.
public class PasswordResetOtp
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    // Denormalised so a lookup by email doesn't need to join Users.
    [MaxLength(120)]
    public string Email { get; set; } = string.Empty;

    // BCrypt hash of the 6-digit code. Never store the code itself.
    [MaxLength(200)]
    public string CodeHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    // Single-use: set the moment a code is successfully redeemed.
    public DateTime? ConsumedAt { get; set; }

    // Failed verification attempts against THIS code.
    public int AttemptCount { get; set; }

    [NotMapped]
    public bool IsActive =>
        ConsumedAt is null
        && AttemptCount < MaxAttempts
        && DateTime.UtcNow < ExpiresAt;

    public const int MaxAttempts = 5;
}
