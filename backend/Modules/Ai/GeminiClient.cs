using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Ai;

// Typed HttpClient for Google's Generative Language API, following the
// Modules/Xero/XeroClient.cs pattern (absolute URL as a const, options injected,
// EnsureConfigured guard, EnsureSuccess folding the body into the message).
public class GeminiClient : IGeminiClient
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiClient(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<string> GenerateFromImageAsync(
        string prompt,
        byte[] fileBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var request = new GeminiRequest
        {
            Contents =
            [
                new GeminiContent
                {
                    // Order matters: text first, then the file — same as the
                    // reference implementation.
                    Parts =
                    [
                        new GeminiPart { Text = prompt },
                        new GeminiPart
                        {
                            InlineData = new GeminiInlineData
                            {
                                MimeType = mimeType,
                                Data = Convert.ToBase64String(fileBytes),
                            },
                        },
                    ],
                },
            ],
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = 0.1,
                MaxOutputTokens = 800,
                ResponseMimeType = "application/json",
                // Gemini 2.5 enables "thinking" by default, which silently eats the
                // entire MaxOutputTokens budget and returns an EMPTY completion.
                // Must be zero or extraction just stops working with no error.
                ThinkingConfig = new GeminiThinkingConfig { ThinkingBudget = 0 },
            },
        };

        return await SendAsync(request, cancellationToken);
    }

    public async Task<string> GenerateTextAsync(
        string systemInstruction,
        IReadOnlyList<GeminiTurn> turns,
        GeminiTextOptions options,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var request = new GeminiRequest
        {
            SystemInstruction = new GeminiContent { Parts = [new GeminiPart { Text = systemInstruction }] },
            Contents = [.. turns.Select(t => new GeminiContent
            {
                Role = t.Role,
                Parts = [new GeminiPart { Text = t.Text }],
            })],
            GenerationConfig = new GeminiGenerationConfig
            {
                Temperature = options.Temperature,
                MaxOutputTokens = options.MaxOutputTokens,
                ResponseMimeType = options.Json ? "application/json" : "text/plain",
                // As above: thinking would eat the output budget.
                ThinkingConfig = new GeminiThinkingConfig { ThinkingBudget = 0 },
            },
        };

        return await SendAsync(request, cancellationToken);
    }

    private async Task<string> SendAsync(GeminiRequest request, CancellationToken cancellationToken)
    {
        var model = string.IsNullOrWhiteSpace(_options.Model) ? "gemini-2.5-flash" : _options.Model.Trim();

        // The key goes in the query string — this endpoint does not take a bearer header.
        var url = $"{BaseUrl}/models/{Uri.EscapeDataString(model)}:generateContent" +
                  $"?key={Uri.EscapeDataString(_options.ApiKey)}";

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
        }
        // Gemini unreachable, or the HttpClient timeout (not the caller giving
        // up) — a provider failure the caller can retry, not a 500.
        catch (Exception ex) when (ex is HttpRequestException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new AiProviderException("Couldn't reach Gemini.", ex);
        }

        using var _ = response;
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<GeminiResponse>(JsonOptions, cancellationToken);
        var text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

        if (string.IsNullOrWhiteSpace(text))
            throw new AiProviderException("Gemini returned an empty completion.");

        return text;
    }

    private void EnsureConfigured()
    {
        // Blank counts as unconfigured, not as a valid empty key — a blank value
        // silently overriding a real one is how the v1 mailer lost a day of sends.
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AiConfigurationException(
                "Gemini is not configured. Set Gemini:ApiKey (dotnet user-secrets set \"Gemini:ApiKey\" \"...\").");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 300) body = body[..300];
        throw new AiProviderException($"Gemini request failed ({(int)response.StatusCode}): {body}");
    }

    // ---- wire shapes ----

    private sealed class GeminiRequest
    {
        [JsonPropertyName("systemInstruction")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GeminiContent? SystemInstruction { get; set; }

        [JsonPropertyName("contents")]
        public List<GeminiContent> Contents { get; set; } = [];

        [JsonPropertyName("generationConfig")]
        public GeminiGenerationConfig? GenerationConfig { get; set; }
    }

    private sealed class GeminiContent
    {
        [JsonPropertyName("role")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Role { get; set; }

        [JsonPropertyName("parts")]
        public List<GeminiPart> Parts { get; set; } = [];
    }

    private sealed class GeminiPart
    {
        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Text { get; set; }

        [JsonPropertyName("inlineData")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GeminiInlineData? InlineData { get; set; }
    }

    private sealed class GeminiInlineData
    {
        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public string Data { get; set; } = string.Empty;
    }

    private sealed class GeminiGenerationConfig
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("maxOutputTokens")]
        public int MaxOutputTokens { get; set; }

        [JsonPropertyName("responseMimeType")]
        public string ResponseMimeType { get; set; } = "application/json";

        [JsonPropertyName("thinkingConfig")]
        public GeminiThinkingConfig? ThinkingConfig { get; set; }
    }

    private sealed class GeminiThinkingConfig
    {
        [JsonPropertyName("thinkingBudget")]
        public int ThinkingBudget { get; set; }
    }

    private sealed class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<GeminiCandidate>? Candidates { get; set; }
    }

    private sealed class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiResponseContent? Content { get; set; }
    }

    private sealed class GeminiResponseContent
    {
        [JsonPropertyName("parts")]
        public List<GeminiResponsePart>? Parts { get; set; }
    }

    private sealed class GeminiResponsePart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
