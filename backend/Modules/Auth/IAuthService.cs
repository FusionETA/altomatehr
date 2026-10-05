namespace AltomateHR.Api.Modules.Auth;

// Business logic for auth. Returns null on failure (bad creds / invalid refresh
// token / not a member of the target org) so the controller decides the HTTP status.
public interface IAuthService
{
    Task<AuthResult?> LoginAsync(string email, string password);

    // The password check alone — no session, no refresh token, no login entry.
    // For a companion app that authenticates someone before minting ITS own
    // session. Null for an unknown email or a wrong password (a failure is
    // still audited, as a failed login is).
    Task<Entities.User?> VerifyPasswordAsync(string email, string password);
    Task<AuthResult?> RefreshAsync(string refreshToken);

    // Re-mint the token for another org the user belongs to. Null = not a member.
    // `sso`: keep the session marked as an Altomate SSO hand-off.
    Task<AuthResult?> SwitchOrgAsync(string userId, string organizationId, bool sso = false);

    // Support mode (Fusioneta superadmins only). Enter: null when the caller is
    // not on SUPERADMIN_EMAILS or the org does not exist. Exit: back to the
    // caller's own org; null when they have none.
    // sso: the current session came through the SSO hand-off. It's carried into
    // the support session and back out, so entering and leaving support mode
    // can't shed it (and with it the SSO-only restrictions).
    Task<AuthResult?> EnterSupportAsync(string userId, string organizationId, bool sso = false);
    Task<AuthResult?> ExitSupportAsync(string userId, bool sso = false);

    // Every org the user can switch into (id + their role there).
    Task<IReadOnlyList<UserOrgDto>> GetOrgsAsync(string userId);

    Task LogoutAsync(string refreshToken);

    // Issues a one-time reset code and emails it. Returns nothing and never
    // signals whether the account exists — the controller always answers 200.
    Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default);

    // Redeems a code and sets the new password. Returns an error message on
    // failure, or null on success.
    Task<string?> ResetPasswordAsync(string email, string otp, string newPassword);

    // Change your own password using the current one. Null on success,
    // otherwise a message safe to show the caller.
    Task<string?> ChangePasswordAsync(string userId, string currentPassword, string newPassword, bool viaSso = false);
}
