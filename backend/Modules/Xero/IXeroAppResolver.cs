namespace AltomateHR.Api.Modules.Xero;

// One Xero app's credentials. AppId null = the original app from the
// server's settings.
public sealed record XeroAppCredentials(string? AppId, string ClientId, string ClientSecret);

// Which app a company should connect through, as its admins see it.
public sealed record XeroAppAssignment(
    // The app a new Connect goes through (null = the original app).
    string? AppId,
    // Standalone with no app added yet: Connect is refused until there is one.
    bool Blocked,
    // True when the company is on its owner's own app rather than the shared one.
    bool OwnApp,
    // The owner's companies don't use Xero at all (also Blocked).
    bool Disabled = false);

// Picks the Xero app for a company. Two different questions, deliberately:
//   - connecting: the app the company's owner is set to now;
//   - everything after (refresh, revoke): the app that ISSUED the tokens,
//     because a refresh token only works with that app — so a company whose
//     owner was moved keeps working on the old app until it reconnects.
public interface IXeroAppResolver
{
    Task<XeroAppAssignment> AssignmentAsync(string organizationId);

    // Throws XeroConnectionException when Connect is blocked.
    Task<XeroAppCredentials> ForConnectAsync(string organizationId);

    Task<XeroAppCredentials> ForAppAsync(string? appId);
}
