namespace AltomateHR.Api.Modules.Auth;

// Business logic for auth. Returns null on failure (bad creds / invalid refresh
// token / not a member of the target org) so the controller decides the HTTP status.
public interface IAuthService
{
    Task<AuthResult?> LoginAsync(string email, string password);
    Task<AuthResult?> RefreshAsync(string refreshToken);

    // Re-mint the token for another org the user belongs to. Null = not a member.
    Task<AuthResult?> SwitchOrgAsync(string userId, string organizationId);

    // Every org the user can switch into (id + their role there).
    Task<IReadOnlyList<UserOrgDto>> GetOrgsAsync(string userId);

    Task LogoutAsync(string refreshToken);

    // Issues a one-time reset code and emails it. Returns nothing and never
    // signals whether the account exists — the controller always answers 200.
    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    // Redeems a code and sets the new password. Returns an error message on
    // failure, or null on success.
    Task<string?> ResetPasswordAsync(string email, string otp, string newPassword);
}
