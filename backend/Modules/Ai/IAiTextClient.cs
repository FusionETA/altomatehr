namespace AltomateHR.Api.Modules.Ai;

// One text-in, JSON-out request to whichever AI provider answers first:
// Claude (Anthropic) by default, Gemini when Claude isn't configured or the
// call fails. Transport only — the caller parses and checks what comes back.
public interface IAiTextClient
{
    Task<AiTextResult> GenerateJsonAsync(
        string systemInstruction, string userText, CancellationToken cancellationToken = default);
}

// Provider is "claude" or "gemini", so the caller can say which answered.
public sealed record AiTextResult(string Text, string Provider);
