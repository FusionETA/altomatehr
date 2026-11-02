using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Fah.Entities;

// One admin's chat with FAH. Private to that person: no one else in the
// company, admins included, reads it.
public class FahConversation : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    // The first question, shortened, so a list of chats can be told apart.
    [MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    // The whole exchange in the model's own message format, exactly as sent and
    // received — tool calls, tool results and the model's signed thinking
    // blocks included, which must go back unchanged on the next turn.
    public string MessagesJson { get; set; } = "[]";

    // A change FAH is holding for the admin's Confirm or Cancel (a serialised
    // FahPendingAction), or null. While set, the conversation's last message
    // is the model's tool call waiting for its answer.
    public string? PendingJson { get; set; }

    // Set while a turn is running, so a second one on the same chat (another
    // tab, a retry after a dropped connection) waits instead of overwriting it.
    // Treated as free once old: a turn cut off by a crash mustn't lock the chat.
    public DateTime? TurnStartedAt { get; set; }

    // Running totals, to see what FAH costs per company. Input is split the way
    // it's billed: fresh, written to the prompt cache, and read from it (cheap).
    public long InputTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long OutputTokens { get; set; }

    // Files attached so far, and the PDF pages among them: the chat's limits
    // (FahFiles.MaxPerChat, MaxPdfPagesPerChat).
    public int AttachmentCount { get; set; }
    public int AttachmentPages { get; set; }

    // When the first image or PDF in the chat is deleted by the model
    // provider (less a margin). The chat can't be continued after it: every
    // message re-sends the whole chat, files included. Null without one.
    public DateTime? FilesExpireAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
