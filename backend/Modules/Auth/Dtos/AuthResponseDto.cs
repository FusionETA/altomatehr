namespace AltomateHR.Api.Modules.Auth.Dtos;

// What login returns to the client: the JWT + a little user info.
public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
