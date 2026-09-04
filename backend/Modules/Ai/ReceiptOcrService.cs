using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Ai.Dtos;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Ai;

// Receipt extraction: builds the prompt, calls Gemini, parses defensively, then
// gates the result server-side. Ported from the v1 implementation, including its
// prompt wording — that text is tuned against real Malaysian receipts, so it is
// kept verbatim rather than paraphrased.
public partial class ReceiptOcrService : IReceiptOcrService
{
    // Below this, the model's own account guess is discarded. The raw score is
    // still returned so a UI could show a weak suggestion differently.
    private const double AccountSuggestionThreshold = 0.8;

    private readonly IGeminiClient _gemini;
    private readonly IChartOfAccountService _accounts;
    private readonly IOrganizationService _organizations;
    private readonly ICurrentUser _currentUser;

    public ReceiptOcrService(
        IGeminiClient gemini,
        IChartOfAccountService accounts,
        IOrganizationService organizations,
        ICurrentUser currentUser)
    {
        _gemini = gemini;
        _accounts = accounts;
        _organizations = organizations;
        _currentUser = currentUser;
    }

    public async Task<ReceiptExtractionDto> AnalyzeAsync(
        byte[] fileBytes,
        string mimeType,
        CancellationToken cancellationToken = default)
    {
        if (fileBytes.Length == 0)
            throw new AiProviderException("Cannot analyze an empty file.");

        // Only offer accounts the employee could actually pick, so the model
        // can't suggest an archived or non-selectable one.
        var candidates = (await _accounts.GetAllAsync())
            .Where(a => a is { IsSelectable: true, IsArchived: false })
            .ToList();

        // Type is non-nullable but lands in a string? Hint slot, and C# won't
        // vary tuple nullability implicitly — hence the cast.
        var prompt = BuildPrompt(candidates.Select(a => (a.Id, a.Name, (string?)a.Type)));
        var raw = await _gemini.GenerateFromImageAsync(prompt, fileBytes, mimeType, cancellationToken);

        var parsed = ParseResponse(raw);

        // ---- gate the account suggestion ----
        var candidateIds = candidates.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var accountId = parsed.SuggestedAccountId;
        if (accountId is null
            || parsed.SuggestedAccountConfidence < AccountSuggestionThreshold
            || !candidateIds.Contains(accountId))
        {
            accountId = null;
        }

        // ---- resolve the currency ----
        var orgId = _currentUser.OrganizationId;
        var defaultCurrency = orgId is null
            ? null
            : (await _organizations.GetByIdAsync(orgId))?.DefaultCurrency;

        var detected = parsed.Currency;
        var currencyUsable = detected is not null && CurrencyCode().IsMatch(detected);
        var resolved = currencyUsable ? detected : defaultCurrency;

        return new ReceiptExtractionDto
        {
            Supplier = parsed.Supplier,
            Total = parsed.Total,
            Date = parsed.Date,
            Description = parsed.Description,
            DetectedCurrency = detected,
            ResolvedCurrency = resolved,
            CurrencyWasOverridden = !currencyUsable && resolved is not null,
            SuggestedAccountId = accountId,
            SuggestedAccountConfidence = parsed.SuggestedAccountConfidence,
            Provider = "gemini",
        };
    }

    // ---- prompt ----

    private static string BuildPrompt(IEnumerable<(string Id, string Name, string? Hint)> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a receipt and invoice parser. Read the attached receipt or invoice (image or PDF) and extract the canonical fields. Multi-page PDFs may contain a single invoice spanning pages — read all pages before deciding the totals.");
        sb.AppendLine();
        sb.AppendLine("Return ONLY a single JSON object matching this exact shape (no prose, no markdown fences):");
        sb.AppendLine();
        sb.AppendLine("{");
        sb.AppendLine("  \"supplier\": \"Merchant or vendor name, or null\",");
        sb.AppendLine("  \"currency\": \"ISO 4217 uppercase (MYR, USD, SGD, EUR, ...) or null if unreadable\",");
        sb.AppendLine("  \"total\": <final total payable as a number, or null>,");
        sb.AppendLine("  \"date\": \"yyyy-mm-dd or null\",");
        sb.AppendLine("  \"description\": \"One short sentence summarising the spend, or null\",");
        sb.AppendLine("  \"suggestedAccountId\": \"id from the candidate list, or null\",");
        sb.AppendLine("  \"suggestedAccountConfidence\": <number between 0 and 1>");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("FIELD-BY-FIELD GUIDANCE:");
        sb.AppendLine();
        sb.AppendLine("supplier — the BUSINESS NAME, almost always at the top of the document. NEVER a cashier/server/host name, table number, invoice number, or generic word like 'RECEIPT' or 'TAX INVOICE'. If you really can't tell, return null.");
        sb.AppendLine();
        sb.AppendLine("total — the GRAND TOTAL the customer paid. Often labeled 'TOTAL', 'AMOUNT', 'GRAND TOTAL', 'AMOUNT DUE', 'BALANCE DUE'. Pick the LAST/largest monetary value before any tip line; never a subtotal or single line item. Return as a plain number (no currency symbol).");
        sb.AppendLine();
        sb.AppendLine("date — the transaction or invoice date. Convert to ISO yyyy-mm-dd. For ambiguous dd/mm vs mm/dd: if either part is >12, it's the day; otherwise assume dd/mm/yyyy.");
        sb.AppendLine();
        sb.AppendLine("currency — ISO 4217 code. RM → MYR, S$ → SGD, HK$ → HKD, NT$ → TWD, A$ → AUD, NZ$ → NZD, C$ → CAD, € → EUR, £ → GBP, ¥ → JPY (or CNY in China). Bare $ is ambiguous → null.");
        sb.AppendLine();
        sb.AppendLine("description — 8 words or less. Lead with WHAT was bought, e.g. 'Coffee and tea at Coffee Shop', 'Lunch at KFC', 'Office stationery'.");
        sb.AppendLine();
        sb.AppendLine("suggestedAccountId — only set when you are clearly confident the spend matches one of the listed accounts. Otherwise null with confidence 0.");
        sb.AppendLine();
        sb.AppendLine("GENERAL RULES:");
        sb.AppendLine("- Return null for any field you can't read confidently. Do NOT guess wildly.");
        sb.AppendLine("- Numbers must be plain JSON numbers (no quotes, no currency symbols).");
        sb.AppendLine();

        var list = candidates.ToList();
        if (list.Count == 0)
        {
            sb.AppendLine("No candidate accounts provided. Set suggestedAccountId to null and suggestedAccountConfidence to 0.");
        }
        else
        {
            sb.AppendLine("Candidate chart-of-accounts (pick at most one matching id, or null):");
            foreach (var (id, name, hint) in list)
            {
                sb.Append($"- id=\"{id}\" name=\"{name}\"");
                if (!string.IsNullOrWhiteSpace(hint)) sb.Append($" ({hint})");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    // ---- parsing ----

    private sealed record ParsedReceipt(
        string? Supplier,
        string? Currency,
        decimal? Total,
        string? Date,
        string? Description,
        string? SuggestedAccountId,
        double SuggestedAccountConfidence);

    // Defensive on purpose. responseMimeType: "application/json" is a strong hint,
    // not a guarantee — models still occasionally wrap output in ```json fences or
    // add a stray sentence. Strip fences, then take the outermost { ... }.
    private static ParsedReceipt ParseResponse(string raw)
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

        return new ParsedReceipt(
            StringOrNull(root, "supplier"),
            NormalizeCurrency(StringOrNull(root, "currency")),
            DecimalOrNull(root, "total"),
            StringOrNull(root, "date"),
            StringOrNull(root, "description"),
            StringOrNull(root, "suggestedAccountId"),
            Clamp01(DoubleOrNull(root, "suggestedAccountConfidence")));
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
    private static string? StringOrNull(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;

        var s = el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return string.Equals(s, "null", StringComparison.OrdinalIgnoreCase) ? null : s;
    }

    private static decimal? DecimalOrNull(JsonElement root, string name)
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

    private static double? DoubleOrNull(JsonElement root, string name)
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

    private static double Clamp01(double? value) => value is null ? 0 : Math.Clamp(value.Value, 0, 1);

    // Pull the first 3-letter run out of whatever came back ("MYR", "myr", "RM (MYR)").
    private static string? NormalizeCurrency(string? value)
    {
        if (value is null) return null;
        var match = CurrencyCode().Match(value.Trim().ToUpperInvariant());
        return match.Success ? match.Value : null;
    }

    // Anchored deliberately. Unanchored, this matched the first three letters
    // ANYWHERE in the value, so any word became a currency: "ringgit" -> "RIN",
    // "cash" -> "CAS". Worse, the same pattern re-checks the normalised value in
    // AnalyzeAsync, so those passed too and the org-default fallback never ran —
    // a claim silently took a garbage currency instead of MYR. The prompt asks
    // for "ISO 4217 uppercase ... or null if unreadable", so an exact code or
    // nothing is the contract.
    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}
