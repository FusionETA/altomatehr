using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Xero.Entities;

namespace AltomateHR.Api.Modules.Xero.Dtos;

// GET /support/xero — every Xero app and every owner's choice, for the
// superadmin. A secret is never sent back; the client id only by its ending.
public class XeroAppsOverviewDto
{
    // What every app must list as its redirect URI in Xero's developer portal.
    public string RedirectUri { get; set; } = string.Empty;
    public XeroOriginalAppDto OriginalApp { get; set; } = new();
    public List<XeroAppDto> Apps { get; set; } = [];
    public List<XeroOwnerGroupDto> Owners { get; set; } = [];
    public List<XeroAppCompanyDto> CompaniesWithoutOwner { get; set; } = [];
}

public class XeroOriginalAppDto
{
    public bool Configured { get; set; }
    public string? ClientIdEnding { get; set; }
    // True while no added app is the shared one: the original app is then shared.
    public bool IsShared { get; set; }
    public int Connections { get; set; }

    // Set once it's been moved into the apps list: the app that took it over.
    public string? MovedToAppId { get; set; }
}

public class XeroAppDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ClientIdEnding { get; set; } = string.Empty;
    public bool IsShared { get; set; }
    // Also serves another product: AltomateHR never removes it from a Xero org.
    public bool UsedByAnotherProduct { get; set; }
    public int Owners { get; set; }
    public int Connections { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class XeroOwnerGroupDto
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public XeroAppChoice Choice { get; set; }
    public string? XeroAppId { get; set; }
    public List<XeroAppCompanyDto> Companies { get; set; } = [];
}

public class XeroAppCompanyDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // Every owner, so a co-owned company can be moved to another's group.
    public List<XeroCompanyOwnerDto> Owners { get; set; } = [];

    // The Xero org it's connected to, and through which app ("original" or an app id).
    public string? ConnectedTenantName { get; set; }
    public string? ConnectedVia { get; set; }
    public bool NeedsReconnect { get; set; }

    // Connected through another app than its owner's choice; moves on reconnect.
    public bool ReconnectToMoveApp { get; set; }
}

public class XeroCompanyOwnerDto
{
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class SaveXeroAppDto
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    // Required when adding; blank on an edit keeps the saved one.
    [StringLength(80)]
    public string? ClientId { get; set; }

    // Required when adding; blank on an edit keeps the saved one.
    [StringLength(200)]
    public string? ClientSecret { get; set; }

    public bool IsShared { get; set; }

    public bool UsedByAnotherProduct { get; set; }
}

public class AdoptOriginalXeroAppDto
{
    [Required, StringLength(80, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public class SetXeroOwnerChoiceDto
{
    public XeroAppChoice Choice { get; set; }
    [StringLength(40)]
    public string? XeroAppId { get; set; }
}

public class SetXeroCompanyOwnerDto
{
    [Required, StringLength(40)]
    public string OwnerUserId { get; set; } = string.Empty;
}
