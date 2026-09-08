using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Notifications.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Notifications;

public class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _db;

    public NotificationRepository(AppDbContext db) => _db = db;

    public async Task<Notification> AddAsync(Notification notification)
    {
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return notification;
    }

    // All queries are additionally auto-scoped to the current org by the
    // global query filter — a user viewing notifications only ever sees the
    // ones raised under their active org.
    public Task<List<Notification>> ListForUserAsync(string userId, int limit = 30) =>
        _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .ToListAsync();

    public Task<int> UnreadCountAsync(string userId) =>
        _db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null);

    public async Task<bool> MarkReadAsync(string userId, string id)
    {
        var affected = await _db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, DateTime.UtcNow));
        return affected > 0;
    }

    public async Task MarkAllReadAsync(string userId)
    {
        await _db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(n => n.ReadAt, DateTime.UtcNow));
    }
}
