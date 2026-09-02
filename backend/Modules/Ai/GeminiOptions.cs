namespace AltomateHR.Api.Modules.Ai;

public class GeminiOptions
{
    public string ApiKey { get; set; } = string.Empty;

    // Override-only. gemini-1.5-flash was retired by Google and now 404s, so the
    // model is config rather than a constant — it can be moved on without a redeploy.
    public string Model { get; set; } = "gemini-2.5-flash";
}
