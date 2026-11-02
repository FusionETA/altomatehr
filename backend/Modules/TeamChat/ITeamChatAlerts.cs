namespace AltomateHR.Api.Modules.TeamChat;

// The team's Bitrix group hears about every sign-in and every new company.
// Both calls are best-effort and never throw: a chat being down must not stop
// anyone signing in or creating a company.
public interface ITeamChatAlerts
{
    // via: how they signed in, e.g. "password" or "Altomate SSO".
    Task SignedInAsync(string userId, string role, string organizationId, string via);

    // via: how it was made, e.g. "self-serve", "Support" or "provisioning API".
    Task CompanyCreatedAsync(string organizationId, string? ownerUserId, string via);
}
