namespace AltomateHR.Api.Modules.Ai;

public interface IGeminiClient
{
    // Sends a text prompt plus one inline image/PDF and returns the model's raw
    // text completion. Parsing is the caller's job — this stays transport-only,
    // matching how IXeroClient returns wire shapes rather than domain objects.
    Task<string> GenerateFromImageAsync(
        string prompt,
        byte[] fileBytes,
        string mimeType,
        CancellationToken cancellationToken = default);

    // A text-only conversation: standing instructions plus the turns so far,
    // oldest first. Returns the model's raw text, like the call above.
    Task<string> GenerateTextAsync(
        string systemInstruction,
        IReadOnlyList<GeminiTurn> turns,
        GeminiTextOptions options,
        CancellationToken cancellationToken = default);
}

// One turn of a conversation. Role is "user" or "model" — Gemini's own words.
public sealed record GeminiTurn(string Role, string Text);

public sealed record GeminiTextOptions(int MaxOutputTokens = 8192, double Temperature = 0.4, bool Json = true);
