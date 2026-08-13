namespace AltomateHR.Api.Modules.Auth;

public interface ITokenService
{
    string CreateToken(string userId, string email, string role, string organizationId);  // access token (JWT)
    string CreateRefreshToken();                                                           // opaque random string
}
