using System.Text.Json;

namespace AltomateHR.Api.Modules.Xero;

// The one sentence a human needs out of a Xero error response.
//
// Xero answers a rejected document by echoing its whole payload back — every
// line item, contact, address and date, several thousand characters of it —
// with the actual reason buried in Elements[].ValidationErrors[].Message near
// the end. Surfacing that raw meant an admin had to read a JSON dump to learn
// "not subscribed to currency USD", and it swamped the claims screen.
//
// Its own class rather than a private helper on XeroClient: it is a pure
// function of the response body, and keeping it separate is what lets it be
// tested against real payloads without standing up an HTTP client.
public static class XeroErrorSummary
{
    // Never throws. An error path that can itself fail on malformed JSON would
    // lose the original failure entirely, so anything unparseable falls back to
    // a trimmed body — still better than nothing.
    public static string Describe(string? body, int statusCode)
    {
        var fallback = $"Xero returned {statusCode}.";
        if (string.IsNullOrWhiteSpace(body)) return fallback;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Trim(body, fallback);

            // The specific ones first: a validation error names the real cause,
            // while the envelope only ever says "A validation exception occurred".
            var reasons = new List<string>();
            if (root.TryGetProperty("Elements", out var elements) &&
                elements.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in elements.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object) continue;
                    if (!element.TryGetProperty("ValidationErrors", out var errors) ||
                        errors.ValueKind != JsonValueKind.Array) continue;

                    foreach (var error in errors.EnumerateArray())
                    {
                        if (error.ValueKind == JsonValueKind.Object &&
                            error.TryGetProperty("Message", out var m) &&
                            m.ValueKind == JsonValueKind.String &&
                            m.GetString() is { Length: > 0 } text &&
                            !reasons.Contains(text))
                        {
                            reasons.Add(text);
                        }
                    }
                }
            }

            if (reasons.Count > 0)
                return string.Join(" ", reasons.Select(EndWithStop));

            // Then the envelope, then OAuth's shape, then give up gracefully.
            foreach (var key in new[] { "Detail", "Message", "Title", "error_description", "error" })
            {
                if (root.TryGetProperty(key, out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    value.GetString() is { Length: > 0 } text)
                {
                    return EndWithStop(text);
                }
            }

            return Trim(body, fallback);
        }
        catch (JsonException)
        {
            return Trim(body, fallback);
        }
    }

    private static string EndWithStop(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 || trimmed.EndsWith('.') || trimmed.EndsWith('!') || trimmed.EndsWith('?')
            ? trimmed
            : trimmed + ".";
    }

    // A body we could not read still beats silence, but not at full length.
    private static string Trim(string body, string fallback)
    {
        var flat = body.Trim();
        if (flat.Length == 0) return fallback;
        return flat.Length <= 200 ? flat : $"{flat[..200]}…";
    }
}
