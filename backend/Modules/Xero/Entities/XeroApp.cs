using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Xero.Entities;

// A Xero app (client id + secret) companies can connect through, registered
// by the superadmin. Belongs to the platform, not a company, so it is never
// company-filtered: only the superadmin endpoints read or write it.
//
// The app in the server's settings (Xero:ClientId) is NOT a row here — it is
// the "original app", the one every connection made before apps existed came
// through (XeroConnection.XeroAppId null).
public class XeroApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string ClientId { get; set; } = string.Empty;

    // Encrypted like the Xero tokens; never sent to any screen.
    public string ClientSecretProtected { get; set; } = string.Empty;

    // The app owners on "Shared" connect through. At most one; with none the
    // original app is the shared one.
    public bool IsShared { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [MaxLength(40)]
    public string? UpdatedByUserId { get; set; }
}

// Which app an owner's companies connect through — set by the superadmin.
public enum XeroAppChoice
{
    // The shared app (the IsShared row, else the original app).
    Shared,
    // The original app from the server's settings.
    Original,
    // XeroAppId; with none set, Connect is blocked until the superadmin adds one.
    Standalone,
}

// One owner's choice. An owner with no row is on Shared. Like XeroApp, it
// belongs to a person, not a company, so it is never company-filtered.
public class XeroOwnerSetting
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OwnerUserId { get; set; } = string.Empty;

    public XeroAppChoice Choice { get; set; } = XeroAppChoice.Shared;

    [MaxLength(40)]
    public string? XeroAppId { get; set; }

    public DateTime UpdatedAt { get; set; }

    [MaxLength(40)]
    public string? UpdatedByUserId { get; set; }
}
