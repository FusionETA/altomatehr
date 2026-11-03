using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Xero.Entities;

public class XeroOAuthState : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(120)]
    public string State { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? ReturnUrl { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    // The Xero tenant this sign-in CONNECTED, set when the callback succeeds.
    // Null for a sign-in that was refused or never completed. A second load of
    // the same callback (refresh, doubled redirect) answers "connected" only
    // while this tenant is still the org's live connection — matching on the
    // connection's dates can't work, since ConnectedAt keeps the FIRST connect.
    [MaxLength(80)]
    public string? ConnectedTenantId { get; set; }

    // The Xero app this sign-in started with (null = the original app), so the
    // callback swaps the code with the same app.
    [MaxLength(40)]
    public string? XeroAppId { get; set; }

    // A sign-in that came back with several Xero orgs and no way to tell which
    // is this company's: Xero's tokens and the orgs to choose from, encrypted
    // (the token protector), until the person who signed in picks one on the
    // Xero card or PendingChoiceExpiresAt passes.
    public string? PendingChoiceProtected { get; set; }
    public DateTime? PendingChoiceExpiresAt { get; set; }
}
