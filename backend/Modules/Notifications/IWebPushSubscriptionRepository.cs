using AltomateHR.Api.Modules.Notifications.Entities;

namespace AltomateHR.Api.Modules.Notifications;

public interface IWebPushSubscriptionRepository
{
    // Create-or-update by endpoint: a browser that re-subscribes (e.g. after
    // clearing site data) gets its keys refreshed rather than a duplicate row.
    Task UpsertAsync(string userId, string endpoint, string p256dh, string auth);

    Task<List<WebPushSubscription>> GetForUserAsync(string userId);

    // Scoped to the owner, same reasoning as NotificationRepository.MarkReadAsync —
    // a forged endpoint can't remove someone else's device registration.
    Task DeleteForUserAsync(string userId, string endpoint);

    // No ownership check: called only when the push provider itself reports the
    // endpoint gone (410/404), which already proves it's dead regardless of whose it was.
    Task DeleteAsync(string id);
}
