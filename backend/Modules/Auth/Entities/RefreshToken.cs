using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AltomateHR.Api.Modules.Auth.Entities;

// A refresh token is STATEFUL — stored in the DB so it can be revoked/rotated.
// (Contrast with the access token, which is stateless: just verified by signature.)
public class RefreshToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(88)]
    public string Token { get; set; } = string.Empty;   // the random secret (unique), also set as the cookie

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    // Email + Role stored so we can re-mint the access token on refresh.
    [MaxLength(120)] public string Email { get; set; } = string.Empty;
    [MaxLength(20)]  public string Role  { get; set; } = string.Empty;
    [MaxLength(40)]  public string OrganizationId { get; set; } = string.Empty;   // re-mint the token with its org

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }             // null = still valid

    [NotMapped]  // computed, not a column
    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
