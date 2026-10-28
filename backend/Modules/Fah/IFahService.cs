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

    FahStatusDto Status();

    Task<IReadOnlyList<FahConversationSummaryDto>> ListAsync();

    // Null when it isn't the caller's chat in this company.
    Task<FahConversationDto?> GetAsync(string id);
}

// Ok with Response; otherwise NotFound (no such chat of theirs), or Error — a
// message to show — with Unavailable when FAH itself is down, not the request.
public sealed record FahResult(
    bool Ok, FahChatResponseDto? Response = null, string? Error = null, bool NotFound = false, bool Unavailable = false);
