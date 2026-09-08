using AltomateHR.Api.Modules.Notifications.Dtos;
using AltomateHR.Api.Modules.Notifications.Entities;

namespace AltomateHR.Api.Modules.Notifications;

// Single funnel for delivering an in-app notification. Feature services
// (claims, leave, attendance) call NotifyAsync when something happens that a
// user should be told about; the header bell reads it back via the other
// three methods.
public interface INotificationService
{
    // Persists a notification for one user. Best-effort and NEVER throws: a
    // notification is a nicety, and it must not be able to fail the approve /
    // submit / reject that triggered it. Returns the new row's id, or null if
    // it no-op'd (missing org/user) or the write failed — internal callers
    // overwhelmingly await-and-discard this; the partner integration is the
    // first caller that needs the id back.
    //
    // `organizationId` is passed explicitly rather than read from ICurrentUser,
    // same reasoning as IRealtimeService.PublishAsync — a future background
    // sweep (e.g. a leave-accrual cron) would have no request context at all.
    Task<string?> NotifyAsync(
        string organizationId, string userId, NotificationType type, string title, string body, string? url = null);

    // For the current caller (read from ICurrentUser, like IRealtimeService.Connect).
    Task<NotificationListDto> GetForCurrentUserAsync();
    Task<bool> MarkReadAsync(string id);
    Task MarkAllReadAsync();
}
