using System.Globalization;

namespace AltomateHR.Api.Common.Tabular;

// One sheet of string cells, format-agnostic. Every exporter in the app builds
// one of these; TabularWriter then renders it as CSV or XLSX. Keeping the
// intermediate representation dumb (strings only) is what lets a single writer
// serve both formats — formatting decisions happen once, here, at build time.
public sealed class TabularSheet
{
    // Excel caps a sheet name at 31 chars and forbids : \ / ? * [ ] — hand it a
    // bad one and ClosedXML throws, so names are sanitised on the way in.
    private const int MaxSheetNameLength = 31;
    private static readonly char[] IllegalSheetNameChars = [':', '\\', '/', '?', '*', '[', ']'];

    private readonly List<IReadOnlyList<string>> _rows = [];

    public TabularSheet(string name, IReadOnlyList<string> headers, string? caption = null)
    {
        Name = SanitizeName(name);
        Headers = headers;
        Caption = caption;
    }

    public string Name { get; }
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<IReadOnlyList<string>> Rows => _rows;

    // A human sentence describing what this sheet covers ("1 Jan – 31 Jan 2026,
    // 42 claims"). Rendered by the PDF writer only: CSV and XLSX put the header
    // row first so the file stays machine-readable and re-importable, and a
    // free-text line above it would break that.
    public string? Caption { get; }

    // An optional final row of totals, kept apart from Rows so the PDF can rule
    // it off and bold it, and so nothing has to guess whether the last row is
    // data. CSV and XLSX append it as an ordinary last row — a spreadsheet with
    // the totals missing would just send someone to a calculator.
    public IReadOnlyList<string>? TotalsRow { get; private set; }

    public TabularSheet AddRow(params string?[] cells)
    {
        _rows.Add(cells.Select(c => c ?? string.Empty).ToList());
        return this;
    }

    public TabularSheet AddRow(IEnumerable<string?> cells)
    {
        _rows.Add(cells.Select(c => c ?? string.Empty).ToList());
        return this;
    }

    public TabularSheet SetTotals(params string?[] cells)
    {
        TotalsRow = cells.Select(c => c ?? string.Empty).ToList();
        return this;
    }

    // ---- Cell formatters ----
    //
    // Every export goes through these so two modules can't disagree about how a
    // date or a decimal looks. All invariant-culture: a European locale's comma
    // decimal separator would collide with the CSV delimiter, and a re-import
    // has to be able to parse back exactly what we wrote.

    public static string Date(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    public static string DateTimeUtc(DateTime? value) =>
        value is null
            ? string.Empty
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Money(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Number(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Number(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    // Minutes → "7.50" hours. Reports are read in hours; storage is in minutes.
    public static string Hours(int minutes) =>
        (minutes / 60d).ToString("0.00", CultureInfo.InvariantCulture);

    public static string Bool(bool value) => value ? "Yes" : "No";

    private static string SanitizeName(string name)
    {
        var cleaned = new string(name.Where(c => !IllegalSheetNameChars.Contains(c)).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Sheet1";
        return cleaned.Length > MaxSheetNameLength ? cleaned[..MaxSheetNameLength] : cleaned;
    }
}
