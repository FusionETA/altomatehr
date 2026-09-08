namespace AltomateHR.Api.Modules.Notifications.Entities;

// What triggered the notification. Deliberately named after the domain event,
// not the surface, so the bell can render a per-type icon later without
// re-deriving it from title text. Mirrors the real app's NotificationType enum
// (globe-engineering-claim's Prisma schema) plus LEAVE_* for this sandbox's
// Leave module, which the reference app doesn't have yet.
public enum NotificationType
{
    CLAIM_SUBMITTED,
    CLAIM_REVIEWED,
    LEAVE_SUBMITTED,
    LEAVE_REVIEWED,
    ATTENDANCE_APPROVAL,
    OVERTIME_SUBMITTED,
    OVERTIME_REVIEWED,

    // Sent by an external partner app (e.g. AppraisifyAlt) via POST
    // /partner/notifications. Deliberately generic: the partner supplies its
    // own title/body directly rather than us templating one per business
    // event, since the partner spec carries no event-type field.
    PARTNER_MESSAGE,
}
