using AltomateHR.Api.Modules.Fah.Entities;

namespace AltomateHR.Api.Modules.Fah;

public interface IFahRepository
{
    // The person's own chat in the current company, or null.
    Task<FahConversation?> GetAsync(string id, string userId);

    // The person's chats here, newest first.
    Task<List<FahConversation>> ListAsync(string userId, int take);

    // Takes the held change off the conversation in one statement, only if it
    // is still `actionId`. True once: a second Confirm (a double click, a
    // retry) finds nothing to take, so a change can't be made twice.
    Task<bool> TryClaimPendingAsync(string id, string userId, string actionId);

    Task AddAsync(FahConversation conversation);
    Task UpdateAsync(FahConversation conversation);
}
