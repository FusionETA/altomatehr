namespace AltomateHR.Api.Common.Tabular;

// The shapes every export in this app speaks.
//
// CSV is the lowest common denominator — no dependency, opens in anything, and
// what payroll usually wants. XLSX is what HR actually reads: a named sheet,
// a frozen bold header row, and no "which delimiter?" dialog.
//
// PDF is WRITE-ONLY, and that asymmetry is the point: it's the format you send
// to someone or file away, not one you get data back out of. Every import path
// therefore rejects it rather than trying (TabularReader.Read, TabularFormats.Detect).
public enum TabularFormat
{
    Csv,
    Xlsx,
    Pdf,
}

public static class TabularFormats
{
    public const string CsvContentType = "text/csv";

    public const string XlsxContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string PdfContentType = "application/pdf";

    public static string ContentType(this TabularFormat format) => format switch
    {
        TabularFormat.Xlsx => XlsxContentType,
        TabularFormat.Pdf => PdfContentType,
        _ => CsvContentType,
    };

    public static string Extension(this TabularFormat format) => format switch
    {
        TabularFormat.Xlsx => "xlsx",
        TabularFormat.Pdf => "pdf",
        _ => "csv",
    };

    // True when the format can be READ back. Import endpoints check this so a
    // PDF is refused with an explanation instead of parsed into nonsense.
    public static bool IsImportable(this TabularFormat format) =>
        format is TabularFormat.Csv or TabularFormat.Xlsx;

    // Reads a `?format=` query value. Anything unrecognised (including null)
    // falls back to CSV rather than 400-ing — an export is a convenience, and
    // a typo shouldn't cost the caller their download.
    public static TabularFormat Parse(string? value) => (value?.Trim().ToLowerInvariant()) switch
    {
        "xlsx" or "excel" => TabularFormat.Xlsx,
        "pdf" => TabularFormat.Pdf,
        _ => TabularFormat.Csv,
    };

    // Detects an UPLOAD's format. Extension first (what the user picked in the
    // save dialog), then the browser-supplied content type as a fallback for
    // files that arrive without one. Null = neither says "spreadsheet", and the
    // caller should refuse rather than guess: handing a .pdf to the CSV parser
    // yields garbage rows instead of an honest error.
    //
    // Never returns Pdf — it is deliberately absent from both lookups below, so
    // a .pdf upload falls through to null and is refused by the caller.
    public static TabularFormat? Detect(string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).TrimStart('.');
        if (string.Equals(extension, "xlsx", StringComparison.OrdinalIgnoreCase)) return TabularFormat.Xlsx;
        if (string.Equals(extension, "csv", StringComparison.OrdinalIgnoreCase)) return TabularFormat.Csv;
        if (string.Equals(extension, "txt", StringComparison.OrdinalIgnoreCase)) return TabularFormat.Csv;

        if (contentType is null) return null;
        if (contentType.Contains("spreadsheetml", StringComparison.OrdinalIgnoreCase)) return TabularFormat.Xlsx;
        if (contentType.Contains("csv", StringComparison.OrdinalIgnoreCase)) return TabularFormat.Csv;
        return null;
    }
}
