using AltomateHR.Api.Modules.Fah.Entities;

namespace AltomateHR.Api.Modules.Fah;

public interface IFahAttachmentRepository
{
    Task AddAsync(FahAttachment attachment);

    // The person's own attachments in the current company that haven't been
    // sent yet, of those asked for.
    Task<List<FahAttachment>> GetUnsentAsync(IReadOnlyCollection<string> ids, string userId);

    // The spreadsheets already sent in a chat of the person's (those with a TableJson).
    Task<List<FahAttachment>> GetSpreadsheetsAsync(string conversationId, string userId);

    // Ties them to the chat their message went into.
    Task MarkSentAsync(IReadOnlyCollection<string> ids, string conversationId);

    // Removes one unsent attachment of the person's; returns it, or null.
    Task<FahAttachment?> RemoveUnsentAsync(string id, string userId);

    // Removes the person's unsent attachments beyond the newest `keep`, and
    // any older than `olderThan`; returns them so their files can be deleted.
    Task<List<FahAttachment>> PruneUnsentAsync(string userId, int keep, DateTime olderThan);
}
