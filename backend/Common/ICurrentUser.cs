namespace AltomateHR.Api.Common;

// Reads the current request's identity from the validated JWT (one place, injectable).
// Services get "who + which org" without touching HttpContext themselves.
public interface ICurrentUser
{
    string? UserId { get; }
    string? OrganizationId { get; }
    string? Role { get; }
    bool IsAdmin { get; }
    bool IsAuthenticated { get; }
}
