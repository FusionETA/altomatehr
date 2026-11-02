using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Fah.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Fah;

public class FahAttachmentRepository : IFahAttachmentRepository
{
    private readonly AppDbContext _db;

    public FahAttachmentRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(FahAttachment attachment)
    {
        _db.FahAttachments.Add(attachment);
        await _db.SaveChangesAsync();
    }

    // The tenant filter keeps it to the current company; the user id to its owner.
    public Task<List<FahAttachment>> GetUnsentAsync(IReadOnlyCollection<string> ids, string userId) =>
        _db.FahAttachments
            .Where(a => ids.Contains(a.Id) && a.UserId == userId && a.ConversationId == null)
            .ToListAsync();

    public Task<List<FahAttachment>> GetSpreadsheetsAsync(string conversationId, string userId) =>
        _db.FahAttachments
            .Where(a => a.ConversationId == conversationId && a.UserId == userId && a.TableJson != null)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();

    public Task MarkSentAsync(IReadOnlyCollection<string> ids, string conversationId) =>
        _db.FahAttachments
            .Where(a => ids.Contains(a.Id) && a.ConversationId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ConversationId, conversationId));

    public async Task<FahAttachment?> RemoveUnsentAsync(string id, string userId)
    {
        var attachment = await _db.FahAttachments
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId && a.ConversationId == null);
        if (attachment is null) return null;
        _db.FahAttachments.Remove(attachment);
        await _db.SaveChangesAsync();
        return attachment;
    }

    public async Task<List<FahAttachment>> PruneUnsentAsync(string userId, int keep, DateTime olderThan)
    {
        var unsent = await _db.FahAttachments
            .Where(a => a.UserId == userId && a.ConversationId == null)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
        var gone = unsent.Where((a, i) => i >= keep || a.CreatedAt < olderThan).ToList();
        if (gone.Count == 0) return gone;
        _db.FahAttachments.RemoveRange(gone);
        await _db.SaveChangesAsync();
        return gone;
    }
}
