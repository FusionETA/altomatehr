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
}
