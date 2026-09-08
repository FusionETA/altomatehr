using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Notifications.Entities;

// A browser's registered Push API endpoint for one user. Named WebPushSubscription
// (not PushSubscription) to avoid colliding with the WebPush NuGet package's own
// PushSubscription class, which WebPushService constructs from these fields at
// send time.
//
// Not tenant-scoped (no ITenantScoped): a device belongs to a login account, not
// an org — the same identity space as User itself, which also has no OrganizationId.
// A user still only ever gets pushed to for events in orgs they belong to, because
// NotifyAsync's caller already resolved that before ever reaching here.
public class WebPushSubscription
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    // The browser's unique push endpoint URL. Unique across all users: the same
    // endpoint can't belong to two accounts, and it's the natural upsert key when
    // a browser re-subscribes (e.g. after clearing site data).
    [MaxLength(500)]
    public string Endpoint { get; set; } = string.Empty;

    [MaxLength(255)]
    public string P256dh { get; set; } = string.Empty;

    [MaxLength(255)]
    public string Auth { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
