namespace AltomateHR.Api.Modules.Ai.Dtos;

// What the OCR pass extracted from a receipt. Everything is nullable — the model
// is instructed to return null rather than guess, and the client treats these
// purely as form pre-fill, never as authoritative values.
public class ReceiptExtractionDto
{
    public string? Supplier { get; set; }
    public decimal? Total { get; set; }
    public string? Date { get; set; }             // yyyy-MM-dd
    public string? Description { get; set; }

    // What the model read off the receipt, before validation.
    public string? DetectedCurrency { get; set; }

    // What the client should actually use: DetectedCurrency when it is a usable
    // code, otherwise the org default.
    public string? ResolvedCurrency { get; set; }
    public bool CurrencyWasOverridden { get; set; }

    // Nulled unless confidence clears the threshold AND the id is one we offered.
    public string? SuggestedAccountId { get; set; }
    public double SuggestedAccountConfidence { get; set; }

    public string Provider { get; set; } = "gemini";
}

// Returned by POST /claims/receipts/analyze — the extraction plus the stored
// file's URL, so one round trip gives the client both.
public class AnalyzeReceiptResponseDto
{
    public string ReceiptUrl { get; set; } = string.Empty;
    public ReceiptExtractionDto Extraction { get; set; } = new();
}
