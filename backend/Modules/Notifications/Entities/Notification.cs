using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Notifications.Entities;

// A persisted in-app notification for exactly one user (the header bell +
// dropdown). Separate from Realtime's RealtimeEventDto: that's an ephemeral
// "something changed, re-fetch" nudge that only reaches an open tab, while
// this is the durable record a user can read, mark read, or come back to
// after logging back in.
//
// OrganizationId is non-null here (unlike the real app's nullable field) —
// this sandbox's ITenantScoped requires it, and every notification is created
// from a known org context (see NotificationService.NotifyAsync's explicit
// organizationId parameter, same convention as IRealtimeService.PublishAsync).
public class Notification : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    // Recipient. Not a foreign key to Employee — UserId is the login account id
    // (same id ICurrentUser.UserId reads from the JWT), so it resolves for any
    // authenticated role, not just employees with a profile.
    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    public NotificationType Type { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;   // long text

    // Relative frontend route to open on click, e.g. "/claims/123". Optional —
    // some notifications are informational only.
    [MaxLength(500)]
    public string? Url { get; set; }

    // Null while unread; set to the read timestamp once acknowledged.
    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
