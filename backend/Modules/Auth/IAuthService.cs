namespace AltomateHR.Api.Modules.Auth;

// Business logic for auth. Returns null on failure (bad creds / invalid refresh token)
// so the controller can decide the HTTP status.
public interface IAuthService
{
    Task<AuthResult?> LoginAsync(string email, string password);
    Task<AuthResult?> RefreshAsync(string refreshToken);
    Task LogoutAsync(string refreshToken);
}
