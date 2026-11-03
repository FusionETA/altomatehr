using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Fah.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Fah;

public class FahRepository : IFahRepository
{
    private readonly AppDbContext _db;

    public FahRepository(AppDbContext db) => _db = db;

    // The tenant filter keeps it to the current company; the user id to its owner.
    public Task<FahConversation?> GetAsync(string id, string userId) =>
        _db.FahConversations.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);

    public Task<List<FahConversation>> ListAsync(string userId, int take) =>
        _db.FahConversations
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.UpdatedAt)
            .Take(take)
            .ToListAsync();

    public async Task<bool> TryClaimPendingAsync(string id, string userId, string actionId)
    {
        // The action id is a GUID inside the stored JSON; matching on it keeps
        // the claim to the card the admin pressed.
        var claimed = await _db.FahConversations
            .Where(c => c.Id == id && c.UserId == userId && c.PendingJson != null && c.PendingJson.Contains(actionId))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.PendingJson, (string?)null));
        return claimed == 1;
    }

    public async Task<bool> TryStartTurnAsync(string id, string userId)
    {
        var now = DateTime.UtcNow;
        var stale = now - StaleTurn;
        return await _db.FahConversations
            .Where(c => c.Id == id && c.UserId == userId && (c.TurnStartedAt == null || c.TurnStartedAt < stale))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.TurnStartedAt, now)) == 1;
    }

    public Task EndTurnAsync(string id) =>
        _db.FahConversations
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.TurnStartedAt, (DateTime?)null));

    // Longer than any real turn (ten rounds of tools and model calls).
    private static readonly TimeSpan StaleTurn = TimeSpan.FromMinutes(10);

    public async Task AddAsync(FahConversation conversation)
    {
        _db.FahConversations.Add(conversation);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(FahConversation conversation)
    {
        _db.FahConversations.Update(conversation);
        await _db.SaveChangesAsync();
    }
}
