using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Xero;

// The superadmin's Xero apps screen: add the apps, pick each owner's, and
// move a co-owned company between its owners. Nothing here touches a live
// connection — a company moved to another app keeps working on the old one
// until an admin reconnects (see IXeroAppResolver).
public class XeroAppsService : IXeroAppsService
{
    private readonly IXeroAppRepository _apps;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IDataProtector _protector;
    private readonly XeroOptions _options;

    public XeroAppsService(
        IXeroAppRepository apps,
        IAuditService audit,
        ICurrentUser currentUser,
        IDataProtectionProvider dataProtection,
        IOptions<XeroOptions> options)
    {
        _apps = apps;
        _audit = audit;
        _currentUser = currentUser;
        _protector = dataProtection.CreateProtector(XeroAppSecrets.Purpose);
        _options = options.Value;
    }

    public async Task<XeroAppsOverviewDto> GetOverviewAsync()
    {
        var apps = await _apps.GetAppsAsync();
        var settings = (await _apps.GetOwnerSettingsAsync())
            .ToDictionary(s => s.OwnerUserId, StringComparer.Ordinal);
        var live = (await _apps.GetConnectionsAsync())
            .Where(c => c.IsConnected)
            .ToDictionary(c => c.OrganizationId, StringComparer.Ordinal);
        var ownerSeats = await _apps.GetOwnerMembershipsAsync();
        var users = (await _apps.GetUsersAsync(ownerSeats.Select(m => m.UserId)))
            .ToDictionary(u => u.Id, StringComparer.Ordinal);
        var ownersByOrg = ownerSeats
            .GroupBy(m => m.OrganizationId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.UserId).ToList(), StringComparer.Ordinal);
        // Once the original is moved into the list, it stands in for "no app" everywhere.
        var moved = MovedTo(apps);
        var shared = apps.FirstOrDefault(a => a.IsShared) ?? moved;

        string? AppOf(string? ownerUserId)
        {
            var setting = ownerUserId is null ? null : settings.GetValueOrDefault(ownerUserId);
            return (setting?.Choice ?? XeroAppChoice.Shared) switch
            {
                XeroAppChoice.Original => moved?.Id,
                XeroAppChoice.Standalone => setting!.XeroAppId,
                _ => shared?.Id,
            };
        }

        bool Blocked(string? ownerUserId) =>
            ownerUserId is not null
            && settings.GetValueOrDefault(ownerUserId) is { } s
            && (s.Choice == XeroAppChoice.None
                || (s.Choice == XeroAppChoice.Standalone && (s.XeroAppId is null || apps.All(a => a.Id != s.XeroAppId))));

        var groups = new Dictionary<string, List<XeroAppCompanyDto>>(StringComparer.Ordinal);
        var withoutOwner = new List<XeroAppCompanyDto>();

        foreach (var org in await _apps.GetOrganizationsAsync())
        {
            var owners = ownersByOrg.GetValueOrDefault(org.Id) ?? [];
            var groupOwner = org.XeroOwnerUserId is { } picked && owners.Contains(picked)
                ? picked
                : owners.FirstOrDefault();
            var connection = live.GetValueOrDefault(org.Id);

            var row = new XeroAppCompanyDto
            {
                Id = org.Id,
                Name = org.Name,
                Owners = [.. owners.Select(id => new XeroCompanyOwnerDto
                {
                    UserId = id,
                    Name = users.GetValueOrDefault(id)?.Name ?? "Unknown",
                })],
                ConnectedTenantName = connection?.TenantName,
                ConnectedVia = connection is null ? null : connection.XeroAppId ?? "original",
                NeedsReconnect = connection?.NeedsReconnect ?? false,
                ReconnectToMoveApp = connection is not null
                                     && !Blocked(groupOwner)
                                     && connection.XeroAppId != AppOf(groupOwner),
            };

            if (groupOwner is null) withoutOwner.Add(row);
            else (groups.TryGetValue(groupOwner, out var list) ? list : groups[groupOwner] = []).Add(row);
        }

        // Only owners a company follows: a setting left behind by someone who
        // no longer owns a company holds nothing up.
        var appIdsInUse = settings.Values
            .Where(s => s.Choice == XeroAppChoice.Standalone && s.XeroAppId is not null && groups.ContainsKey(s.OwnerUserId))
            .GroupBy(s => s.XeroAppId!)
            .ToDictionary(g => g.Key, g => g.Count());

        return new XeroAppsOverviewDto
        {
            RedirectUri = _options.RedirectUri,
            OriginalApp = new XeroOriginalAppDto
            {
                Configured = !string.IsNullOrWhiteSpace(_options.ClientId)
                             && !string.IsNullOrWhiteSpace(_options.ClientSecret),
                ClientIdEnding = Ending(_options.ClientId),
                IsShared = shared is null,
                Connections = live.Values.Count(c => c.XeroAppId is null),
                MovedToAppId = moved?.Id,
            },
            Apps = [.. apps.Select(a => new XeroAppDto
            {
                Id = a.Id,
                Name = a.Name,
                ClientIdEnding = Ending(a.ClientId) ?? string.Empty,
                IsShared = a.IsShared,
                UsedByAnotherProduct = a.UsedByAnotherProduct,
                Owners = appIdsInUse.GetValueOrDefault(a.Id),
                Connections = live.Values.Count(c => c.XeroAppId == a.Id),
                UpdatedAt = a.UpdatedAt,
            })],
            Owners = [.. groups
                .Select(g =>
                {
                    var user = users.GetValueOrDefault(g.Key);
                    var setting = settings.GetValueOrDefault(g.Key);
                    return new XeroOwnerGroupDto
                    {
                        UserId = g.Key,
                        Name = user?.Name ?? "Unknown",
                        Email = user?.Email ?? string.Empty,
                        Choice = setting?.Choice ?? XeroAppChoice.Shared,
                        XeroAppId = setting?.XeroAppId,
                        Companies = [.. g.Value.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)],
                    };
                })
                .OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase)],
            CompaniesWithoutOwner = withoutOwner,
        };
    }

    public async Task<XeroAppDto> AddAppAsync(SaveXeroAppDto dto)
    {
        var (name, clientId) = Clean(dto, keepClientId: null);
        var secret = dto.ClientSecret?.Trim();
        if (string.IsNullOrEmpty(secret))
            throw new ArgumentException("Enter the app's client secret.");
        await EnsureClientIdFreeAsync(clientId, exceptId: null);

        var now = DateTime.UtcNow;
        var app = new XeroApp
        {
            Name = name,
            ClientId = clientId,
            ClientSecretProtected = _protector.Protect(secret),
            UsedByAnotherProduct = dto.UsedByAnotherProduct,
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = _currentUser.UserId,
        };
        await _apps.AddAppAsync(app);
        if (dto.IsShared) await _apps.SetSharedAppAsync(app.Id);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroAppSave,
            $"Xero app \"{app.Name}\" added{(dto.IsShared ? " as the shared app" : "")}",
            TargetType: "XeroApp",
            TargetId: app.Id,
            Support: Actor()));

        return await GetAppDtoAsync(app.Id);
    }

    // The original app lives in the server settings, where nothing can rename
    // or rotate it. This copies it into the apps list under the same client id
    // and secret, so every connection made through it keeps refreshing with no
    // reconnect, and moves those connections and its owners onto the copy.
    public async Task<XeroAppDto> AdoptOriginalAppAsync(string name)
    {
        var clientId = _options.ClientId?.Trim();
        var secret = _options.ClientSecret?.Trim();
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(secret))
            throw new ArgumentException("The original app isn't set up in the server settings.");
        var apps = await _apps.GetAppsAsync();
        if (MovedTo(apps) is { } existing)
            throw new ArgumentException($"The original app is already in the list as \"{existing.Name}\".");
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Give the app a name.");

        var now = DateTime.UtcNow;
        var app = new XeroApp
        {
            Name = name,
            ClientId = clientId,
            ClientSecretProtected = _protector.Protect(secret),
            // It was the shared app while no other was: keep it that way.
            IsShared = apps.All(a => !a.IsShared),
            CreatedAt = now,
            UpdatedAt = now,
            UpdatedByUserId = _currentUser.UserId,
        };
        await _apps.AdoptOriginalAppAsync(app);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroAppSave,
            $"Original Xero app moved into the apps list as \"{app.Name}\"",
            TargetType: "XeroApp",
            TargetId: app.Id,
            Support: Actor()));

        return await GetAppDtoAsync(app.Id);
    }

    public async Task<XeroAppDto?> UpdateAppAsync(string id, SaveXeroAppDto dto)
    {
        var app = await _apps.GetAppAsync(id);
        if (app is null) return null;

        var (name, clientId) = Clean(dto, keepClientId: app.ClientId);
        var secret = dto.ClientSecret?.Trim();

        // A different client id is a different app: tokens it issued can't be
        // refreshed with another's, so it must come with its own secret.
        var clientChanged = !string.Equals(clientId, app.ClientId, StringComparison.Ordinal);
        if (clientChanged)
        {
            if (string.IsNullOrEmpty(secret))
                throw new ArgumentException("A new client id needs its client secret too.");
            await EnsureClientIdFreeAsync(clientId, exceptId: app.Id);
            if ((await _apps.GetConnectionsAsync()).Any(c => c.IsConnected && c.XeroAppId == app.Id))
                throw new ArgumentException(
                    "Companies are connected through this app, so its client id can't change. Add the new app separately.");
        }

        app.Name = name;
        app.ClientId = clientId;
        app.UsedByAnotherProduct = dto.UsedByAnotherProduct;
        if (!string.IsNullOrEmpty(secret)) app.ClientSecretProtected = _protector.Protect(secret);
        app.UpdatedAt = DateTime.UtcNow;
        app.UpdatedByUserId = _currentUser.UserId;
        await _apps.UpdateAppAsync(app);

        if (dto.IsShared && !app.IsShared) await _apps.SetSharedAppAsync(app.Id);
        else if (!dto.IsShared && app.IsShared) await _apps.SetSharedAppAsync(null);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroAppSave,
            $"Xero app \"{app.Name}\" updated{(string.IsNullOrEmpty(secret) ? "" : " (new secret)")}",
            TargetType: "XeroApp",
            TargetId: app.Id,
            Support: Actor()));

        return await GetAppDtoAsync(app.Id);
    }

    public async Task<(bool NotFound, string? Error)> DeleteAppAsync(string id)
    {
        var app = await _apps.GetAppAsync(id);
        if (app is null) return (true, null);

        if ((await _apps.GetConnectionsAsync()).Any(c => c.IsConnected && c.XeroAppId == id))
            return (false, "Companies are still connected through this app. Move them and let them reconnect first.");
        if ((await GetOverviewAsync()).Owners.Any(o => o.XeroAppId == id && o.Choice == XeroAppChoice.Standalone))
            return (false, "An owner is still set to this app. Move them to another app first.");

        await _apps.DeleteAppAsync(app);
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroAppDelete,
            $"Xero app \"{app.Name}\" deleted",
            TargetType: "XeroApp",
            TargetId: app.Id,
            Support: Actor()));
        return (false, null);
    }

    public async Task SetOwnerChoiceAsync(string ownerUserId, SetXeroOwnerChoiceDto dto)
    {
        var overview = await GetOverviewAsync();
        var group = overview.Owners.FirstOrDefault(o => o.UserId == ownerUserId)
            ?? throw new ArgumentException("That person doesn't own any company.");

        string? appId = null;
        string label;
        switch (dto.Choice)
        {
            case XeroAppChoice.Standalone:
                // An app may be left out: Connect is then blocked until one is set.
                if (!string.IsNullOrWhiteSpace(dto.XeroAppId))
                {
                    var app = await _apps.GetAppAsync(dto.XeroAppId)
                        ?? throw new ArgumentException("That Xero app doesn't exist.");
                    appId = app.Id;
                    label = $"their own app \"{app.Name}\"";
                }
                else label = "their own app (not added yet)";
                break;
            case XeroAppChoice.Original:
                label = "the original app";
                break;
            case XeroAppChoice.None:
                label = "no app";
                break;
            default:
                label = "the shared app";
                break;
        }

        var setting = await _apps.GetOwnerSettingAsync(ownerUserId)
            ?? new XeroOwnerSetting { OwnerUserId = ownerUserId };
        setting.Choice = dto.Choice;
        setting.XeroAppId = appId;
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedByUserId = _currentUser.UserId;
        await _apps.SaveOwnerSettingAsync(setting);

        // In each company's own log: it changes how that company connects.
        foreach (var company in group.Companies)
        {
            await _audit.WriteAsync(new AuditEvent(
                AuditActions.XeroAppChoiceSet,
                dto.Choice == XeroAppChoice.None
                    ? "Xero switched off for this company"
                    : $"Xero now connects through {label}",
                TargetType: "Organization",
                TargetId: company.Id,
                OrganizationId: company.Id,
                Support: Actor()));
        }
    }

    public async Task<(bool NotFound, string? Error)> SetCompanyOwnerAsync(string organizationId, string ownerUserId)
    {
        var org = await _apps.GetOrganizationAsync(organizationId);
        if (org is null) return (true, null);

        var isOwner = (await _apps.GetOwnerMembershipsAsync())
            .Any(m => m.OrganizationId == organizationId && m.UserId == ownerUserId);
        if (!isOwner) return (false, "That person isn't an owner of this company.");

        org.XeroOwnerUserId = ownerUserId;
        await _apps.UpdateOrganizationAsync(org);

        var name = (await _apps.GetUsersAsync([ownerUserId])).FirstOrDefault()?.Name ?? ownerUserId;
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.XeroAppChoiceSet,
            $"Xero now follows {name}'s app choice",
            TargetType: "Organization",
            TargetId: org.Id,
            OrganizationId: org.Id,
            Support: Actor()));
        return (false, null);
    }

    // Fusioneta staff acting from the Support page: shown in a company's log as
    // "System (Support)", with the real person in the internal support log.
    private SupportActor? Actor() =>
        _currentUser.UserId is { } id ? new SupportActor(id, _currentUser.Email ?? string.Empty) : null;

    // The app the original was moved into: the one carrying its client id.
    private XeroApp? MovedTo(IEnumerable<XeroApp> apps) =>
        string.IsNullOrWhiteSpace(_options.ClientId)
            ? null
            : apps.FirstOrDefault(a => string.Equals(a.ClientId, _options.ClientId.Trim(), StringComparison.Ordinal));

    private async Task<XeroAppDto> GetAppDtoAsync(string id) =>
        (await GetOverviewAsync()).Apps.First(a => a.Id == id);

    private async Task EnsureClientIdFreeAsync(string clientId, string? exceptId)
    {
        // The list first: once the original is moved in, it's simply "another app".
        if ((await _apps.GetAppsAsync()).Any(a => a.Id != exceptId && a.ClientId == clientId))
            throw new ArgumentException("Another app already uses that client id.");
        if (string.Equals(clientId, _options.ClientId?.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("That's the original app's client id. Use \"Move into the apps list\" on the original app instead.");
    }

    private static (string Name, string ClientId) Clean(SaveXeroAppDto dto, string? keepClientId)
    {
        var name = dto.Name.Trim();
        var clientId = string.IsNullOrWhiteSpace(dto.ClientId) ? keepClientId ?? "" : dto.ClientId.Trim();
        if (name.Length == 0) throw new ArgumentException("Give the app a name.");
        if (clientId.Length < 8 || clientId.Any(char.IsWhiteSpace))
            throw new ArgumentException("Enter the client id exactly as Xero shows it.");
        return (name, clientId);
    }

    // The last 4 characters, so the superadmin can tell apps apart without the id on screen.
    private static string? Ending(string? clientId) =>
        string.IsNullOrWhiteSpace(clientId) ? null : $"…{clientId.Trim()[^Math.Min(4, clientId.Trim().Length)..]}";
}
