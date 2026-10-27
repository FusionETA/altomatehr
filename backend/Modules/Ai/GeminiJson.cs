using System.Globalization;
using System.Text.Json;

namespace AltomateHR.Api.Modules.Ai;

// Reading the JSON object an image prompt asks Gemini for. Defensive on
// purpose: responseMimeType "application/json" is a strong hint, not a
// guarantee — models still occasionally wrap output in ```json fences or add a
// stray sentence, and emit "" or "null" where they were told to emit null.
internal static class GeminiJson
{
    // Strip fences, then take the outermost { ... }.
    public static JsonElement ParseObject(string raw)
    {
        var text = StripCodeFences(raw).Trim();

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start >= 0 && end > start) text = text[start..(end + 1)];

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            var snippet = raw.Length > 200 ? raw[..200] : raw;
            throw new AiProviderException($"Gemini returned non-JSON: {ex.Message}. Raw: {snippet}");
        }

        if (root.ValueKind != JsonValueKind.Object)
            throw new AiProviderException("Gemini returned non-object JSON.");

        return root;
    }

    private static string StripCodeFences(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;

        var firstNewline = trimmed.IndexOf('\n');
        if (firstNewline < 0) return trimmed;

        trimmed = trimmed[(firstNewline + 1)..];
        var closing = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return closing >= 0 ? trimmed[..closing] : trimmed;
    }

    // The model is told to emit null, but it sometimes emits "" or the string
    // "null" instead — treat all three the same.
    public static string? StringOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;

        var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return string.Equals(s, "null", StringComparison.OrdinalIgnoreCase) ? null : s;
    }

    public static decimal? DecimalOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var d)) return d;

        // Numbers sometimes come back quoted despite the instruction.
        if (el.ValueKind == JsonValueKind.String
            && decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static double? DoubleOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out var d)) return d;
        if (el.ValueKind == JsonValueKind.String
            && double.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public static bool? BoolOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(el.GetString(), out var b) => b,
            _ => null,
        };
    }
}
