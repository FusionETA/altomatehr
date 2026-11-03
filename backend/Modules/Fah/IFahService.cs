using AltomateHR.Api.Modules.Fah.Dtos;

namespace AltomateHR.Api.Modules.Fah;

public interface IFahService
{
    // One turn: the admin's message in, FAH's answer out. The conversation is
    // saved either way, so a failed turn can be retried from where it stopped.
    Task<FahResult> ChatAsync(FahChatRequestDto dto, Func<FahProgressEvent, Task>? progress, CancellationToken ct);

    // The admin's answer to a held change: Confirm makes it, Cancel drops it.
    // Either way FAH then says what happened.
    Task<FahResult> ConfirmAsync(FahConfirmRequestDto dto, Func<FahProgressEvent, Task>? progress, CancellationToken ct);

    // A file for the next message: read and stored, not yet sent.
    Task<FahUploadResult> UploadAsync(string? fileName, byte[] content, CancellationToken ct);

    // Takes back an uploaded file that hasn't been sent. False if there's none.
    Task<bool> RemoveAttachmentAsync(string id);

    // The AltomateHR import file for an import card still waiting in one of
    // the caller's chats, or null.
    Task<Common.Tabular.TabularExportResult?> ImportFileAsync(string conversationId, string actionId);

    FahStatusDto Status();

    Task<IReadOnlyList<FahConversationSummaryDto>> ListAsync();

    // Null when it isn't the caller's chat in this company.
    Task<FahConversationDto?> GetAsync(string id);
}

// Ok with Response; otherwise NotFound (no such chat of theirs), or Error — a
// message to show — with Unavailable when FAH itself is down, not the request.
// NotSent: the message isn't in the chat (its files are still unsent), so the
// panel can put it back to be sent again.
public sealed record FahResult(
    bool Ok,
    FahChatResponseDto? Response = null,
    string? Error = null,
    bool NotFound = false,
    bool Unavailable = false,
    bool NotSent = false);

public sealed record FahUploadResult(FahAttachmentDto? Attachment, string? Error);
