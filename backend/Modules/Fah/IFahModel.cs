using System.Text.Json;

namespace AltomateHR.Api.Modules.Fah;

// The language model behind FAH, one turn at a time. Messages go in and come
// out in the model's own wire format (raw JSON), so a conversation can be
// stored and replayed exactly — signed thinking blocks included.
public interface IFahModel
{
    // Whether the model can be called at all (an API key is configured).
    bool IsConfigured { get; }

    Task<FahModelTurn> NextAsync(
        string system,
        IReadOnlyList<FahTool> tools,
        IReadOnlyList<JsonElement> messages,
        CancellationToken ct);

    // Stores an image or PDF with the model provider, to be referred to by the
    // returned id in later messages. The provider deletes it after `keepFor`.
    Task<string> UploadFileAsync(string fileName, string mediaType, byte[] content, TimeSpan keepFor, CancellationToken ct);

    // Deletes a stored file now. Best effort: a file already gone is fine.
    Task DeleteFileAsync(string fileId, CancellationToken ct);
}

public sealed record FahTool(string Name, string Description, JsonElement InputSchema);

// AssistantMessage is the reply to append to the conversation as-is:
// {"role":"assistant","content":[...]}.
public sealed record FahModelTurn(
    JsonElement AssistantMessage,
    string StopReason,
    IReadOnlyList<FahToolUse> ToolUses,
    string Text,
    long InputTokens,
    long OutputTokens,
    long CacheWriteTokens = 0,
    long CacheReadTokens = 0);

public sealed record FahToolUse(string Id, string Name, JsonElement Input);

// No API key configured, or the model service refused or failed. Rejected:
// the service refused the request itself as invalid (not a passing outage) —
// for a message with files, most likely a file it couldn't take.
public sealed class FahModelException(string message, Exception? inner = null, bool rejected = false)
    : Exception(message, inner)
{
    public bool Rejected { get; } = rejected;
}
