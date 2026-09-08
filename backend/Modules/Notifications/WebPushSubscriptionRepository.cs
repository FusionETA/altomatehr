using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Notifications.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Notifications;

public class WebPushSubscriptionRepository : IWebPushSubscriptionRepository
{
    private readonly AppDbContext _db;

    public WebPushSubscriptionRepository(AppDbContext db) => _db = db;

    public async Task UpsertAsync(string userId, string endpoint, string p256dh, string auth)
    {
        var existing = await _db.WebPushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint);
        if (existing is not null)
        {
            existing.UserId = userId;
            existing.P256dh = p256dh;
            existing.Auth = auth;
        }
        else
        {
            _db.WebPushSubscriptions.Add(new WebPushSubscription
            {
                UserId = userId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await _db.SaveChangesAsync();
    }

    public Task<List<WebPushSubscription>> GetForUserAsync(string userId) =>
        _db.WebPushSubscriptions.Where(s => s.UserId == userId).ToListAsync();

    public async Task DeleteForUserAsync(string userId, string endpoint)
    {
        await _db.WebPushSubscriptions
            .Where(s => s.UserId == userId && s.Endpoint == endpoint)
            .ExecuteDeleteAsync();
    }

    public async Task DeleteAsync(string id)
    {
        await _db.WebPushSubscriptions.Where(s => s.Id == id).ExecuteDeleteAsync();
    }
}
