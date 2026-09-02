using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Email;

// EngineMailer transactional email.
//
// Two non-obvious things about this API, both learned the hard way in v1:
//   1. Auth is a plain "APIKey: <key>" header — NOT "Authorization: Bearer".
//      The key is the whole credential; there is no account id alongside it.
//   2. HTTP 200 does not mean accepted. The real outcome sits in a Result
//      object in the body, so the status code alone would report success for
//      mail that was never sent.
//      Shape observed live on 2026-09-01: {"Result":{"StatusCode":"200",
//      "Status":"OK","TransactionID":"..."}} — a plain object. v1 additionally
//      saw a DOUBLE-ENCODED form (a JSON string containing that JSON), so the
//      parser below unwraps that case too rather than assuming one shape.
public class EngineMailerEmailSender : IEmailSender
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private readonly EmailOptions _options;
    private readonly ILogger<EngineMailerEmailSender> _logger;

    public EngineMailerEmailSender(
        HttpClient http,
        IOptions<EmailOptions> options,
        ILogger<EngineMailerEmailSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<bool> SendAsync(
        string toEmail,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken = default)
    {
        if (!_options.IsConfigured)
        {
            // Shouldn't happen — Program.cs registers LoggingEmailSender instead
            // when unconfigured — but never fail open into a silent no-op.
            _logger.LogError("EngineMailer is not configured (EngineMailer:ApiKey / FromEmail).");
            return false;
        }

        var request = new SendEmailRequest
        {
            ApiKey = _options.ApiKey,
            SubmittedBy = _options.FromName,
            SenderEmail = _options.FromEmail,
            SenderName = _options.FromName,
            Subject = subject,
            Body = htmlBody,
            ToEmail = toEmail,
        };

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = JsonContent.Create(request, options: JsonOptions),
            };
            message.Headers.Add("APIKey", _options.ApiKey);

            using var response = await _http.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "EngineMailer returned {Status}: {Body}",
                    (int)response.StatusCode, Truncate(body));
                return false;
            }

            var (accepted, detail) = ParseEngineMailerBody(body);
            if (!accepted)
            {
                _logger.LogError("EngineMailer rejected the message: {Detail}", Truncate(detail ?? body));
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A transport failure must not take down the calling flow.
            _logger.LogError(ex, "EngineMailer send failed.");
            return false;
        }
    }

    // Handles both observed shapes: a plain {"Result":{...}} object, and the
    // double-encoded variant where the whole thing arrives as a JSON *string*.
    // Returns (accepted, detail).
    internal static (bool Accepted, string? Detail) ParseEngineMailerBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (false, "empty response body");

        try
        {
            using var outer = JsonDocument.Parse(body);

            // Unwrap the double encoding when the payload is a bare JSON string.
            var inner = outer.RootElement.ValueKind == JsonValueKind.String
                ? JsonDocument.Parse(outer.RootElement.GetString() ?? "{}")
                : null;

            var root = inner?.RootElement ?? outer.RootElement;
            try
            {
                if (root.ValueKind != JsonValueKind.Object) return (false, body);

                // The real outcome lives under "Result" when present.
                var result = root.TryGetProperty("Result", out var r) && r.ValueKind == JsonValueKind.Object
                    ? r
                    : root;

                var statusCode = ReadString(result, "StatusCode");
                var status = ReadString(result, "Status");

                // EngineMailer reports its own status as a string code; 200/OK is
                // the only accepted outcome.
                var accepted =
                    string.Equals(statusCode, "200", StringComparison.Ordinal) ||
                    string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase);

                return accepted ? (true, null) : (false, result.ToString());
            }
            finally
            {
                inner?.Dispose();
            }
        }
        catch (JsonException)
        {
            return (false, body);
        }
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var el)
            ? el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString()
            : null;

    private static string Truncate(string value) => value.Length > 300 ? value[..300] : value;

    private sealed class SendEmailRequest
    {
        [JsonPropertyName("APIKey")]
        public string ApiKey { get; set; } = string.Empty;

        [JsonPropertyName("SubmittedBy")]
        public string SubmittedBy { get; set; } = string.Empty;

        // Must be a VERIFIED sender on EngineMailer's side or the send is
        // rejected regardless of anything in this code.
        [JsonPropertyName("SenderEmail")]
        public string SenderEmail { get; set; } = string.Empty;

        [JsonPropertyName("SenderName")]
        public string SenderName { get; set; } = string.Empty;

        [JsonPropertyName("Subject")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("Body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("ToEmail")]
        public string ToEmail { get; set; } = string.Empty;
    }
}
