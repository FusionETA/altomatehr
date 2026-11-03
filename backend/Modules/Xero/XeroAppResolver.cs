using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Xero;

public class XeroAppResolver : IXeroAppResolver
{
    public const string BlockedMessage =
        "Xero isn't set up for your company yet. Contact AltomateHR to finish setting up your company's Xero app.";

    public const string DisabledMessage =
        "Xero isn't switched on for your company. Contact AltomateHR if you need it.";

    private readonly IXeroAppRepository _apps;
    private readonly IDataProtector _protector;
    private readonly XeroOptions _options;

    public XeroAppResolver(
        IXeroAppRepository apps,
        IDataProtectionProvider dataProtection,
        IOptions<XeroOptions> options)
    {
        _apps = apps;
        _protector = dataProtection.CreateProtector(XeroAppSecrets.Purpose);
        _options = options.Value;
    }

    public async Task<XeroAppAssignment> AssignmentAsync(string organizationId)
    {
        var owner = await _apps.GetXeroOwnerOfAsync(organizationId);
        var setting = owner is null ? null : await _apps.GetOwnerSettingAsync(owner);

        switch (setting?.Choice ?? XeroAppChoice.Shared)
        {
            case XeroAppChoice.Original:
                return new XeroAppAssignment((await MovedOriginalAsync())?.Id, Blocked: false, OwnApp: true);

            case XeroAppChoice.None:
                return new XeroAppAssignment(null, Blocked: true, OwnApp: false, Disabled: true);

            case XeroAppChoice.Standalone:
                var app = setting!.XeroAppId is { } id ? await _apps.GetAppAsync(id) : null;
                return app is null
                    ? new XeroAppAssignment(null, Blocked: true, OwnApp: true)
                    : new XeroAppAssignment(app.Id, Blocked: false, OwnApp: true);

            default:
                // With no shared app added yet, the original app is the shared one.
                var shared = await _apps.GetSharedAppAsync() ?? await MovedOriginalAsync();
                return new XeroAppAssignment(shared?.Id, Blocked: false, OwnApp: false);
        }
    }

    public async Task<XeroAppCredentials> ForConnectAsync(string organizationId)
    {
        var assignment = await AssignmentAsync(organizationId);
        if (assignment.Blocked)
            throw new XeroConnectionException(assignment.Disabled ? DisabledMessage : BlockedMessage);
        return await ForAppAsync(assignment.AppId);
    }

    public async Task<bool> KeepOnXeroAsync(string? appId) =>
        appId is not null && (await _apps.GetAppAsync(appId))?.UsedByAnotherProduct == true;

    public async Task<XeroAppCredentials> ForAppAsync(string? appId)
    {
        if (appId is null)
        {
            // Once moved into the list, "the original app" IS that app — one
            // client id must never count as two apps, or a reconnect would
            // revoke the company's own live connection as "the old app's".
            return await MovedOriginalAsync() is { } moved
                ? new XeroAppCredentials(moved.Id, moved.ClientId, _protector.Unprotect(moved.ClientSecretProtected))
                : new XeroAppCredentials(null, _options.ClientId, _options.ClientSecret);
        }

        // Deleting an app that a connection still uses is refused, so this
        // only happens if the row was removed by hand.
        var app = await _apps.GetAppAsync(appId)
            ?? throw new XeroReconnectRequiredException(
                "The Xero app this connection was made through no longer exists. Reconnect Xero to continue.");

        return new XeroAppCredentials(app.Id, app.ClientId, _protector.Unprotect(app.ClientSecretProtected));
    }

    // The app the original (server settings) was moved into: the one carrying its client id.
    private async Task<XeroApp?> MovedOriginalAsync()
    {
        var clientId = _options.ClientId?.Trim();
        if (string.IsNullOrEmpty(clientId)) return null;
        return (await _apps.GetAppsAsync()).FirstOrDefault(a => string.Equals(a.ClientId, clientId, StringComparison.Ordinal));
    }
}

// The key purpose the app secrets are encrypted under — its own, apart from
// the Xero tokens'.
public static class XeroAppSecrets
{
    public const string Purpose = "AltomateHR.XeroAppSecrets.v1";
}
