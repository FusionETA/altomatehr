using AltomateHR.Api.Modules.Notifications.Entities;

namespace AltomateHR.Api.Modules.Notifications;

public interface INotificationRepository
{
    Task<Notification> AddAsync(Notification notification);

    // Most-recent notifications for a user, newest first. `limit` caps the
    // list so the bell dropdown stays light.
    Task<List<Notification>> ListForUserAsync(string userId, int limit = 30);

    Task<int> UnreadCountAsync(string userId);

    // Marks one notification read, scoped to its owner so a forged id can't
    // acknowledge someone else's notification. Returns whether a row matched.
    Task<bool> MarkReadAsync(string userId, string id);

    Task MarkAllReadAsync(string userId);
}
