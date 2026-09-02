using System.Globalization;

namespace AltomateHR.Api.Common.Tabular;

// One column of an import template: what it's called, whether it's mandatory,
// and what a good value looks like. The same declaration drives BOTH the
// downloadable template and the header matching on upload — so a template can't
// drift out of sync with the parser that reads it back.
//
// `Aliases` exist because real files come from other systems: a Jibble export
// says "Member", ours says "Employee Email". Matching is normalised (case,
// spaces and punctuation stripped, a leading `*` ignored), so only genuinely
// different WORDS need listing here.
public sealed record TabularColumn(
    string Key,
    string Label,
    bool Required,
    string Example,
    string[]? Aliases = null);

public sealed record TabularImportError(int Row, string Message);

// The outcome of an import, in the three buckets an admin actually cares about:
// what landed, what was already there, and what needs fixing.
//
// Skipped is NOT a failure — every importer here is append-only and idempotent,
// so re-uploading the same file to catch a few fixed rows is safe and reports
// the rest as skipped rather than duplicating them.
public sealed class TabularImportResult
{
    public int Imported { get; private set; }
    public int Skipped { get; private set; }
    public int Failed { get; private set; }
    public List<TabularImportError> Errors { get; } = [];

    // Row numbers are 1-based AS THE ADMIN SEES THEM in their spreadsheet
    // (header = row 1, first data row = row 2). Row 1 is also where file-level
    // problems land — a missing column is a header problem.
    public const int HeaderRow = 1;

    public void CountImported() => Imported++;
    public void CountSkipped() => Skipped++;

    public void Fail(int row, string message)
    {
        Failed++;
        // Cap the error list: a wholly mismatched file would otherwise return one
        // error per row, and nobody reads 20,000 of them.
        if (Errors.Count < 200) Errors.Add(new TabularImportError(row, message));
    }

    // A problem with the file itself (empty, no header, missing column) — nothing
    // was read, so it isn't counted against any data row.
    public static TabularImportResult FileError(string message)
    {
        var result = new TabularImportResult();
        result.Errors.Add(new TabularImportError(HeaderRow, message));
        return result;
    }
}

// Header row → column index, tolerant of label/alias/case/punctuation variation.
public sealed class TabularHeaderMap
{
    private readonly Dictionary<string, int> _byKey;

    private TabularHeaderMap(Dictionary<string, int> byKey) => _byKey = byKey;

    // Builds the map, or returns the list of required columns that are missing so
    // the caller can name them all at once (one upload, one complete fix-list).
    //
    // `anyOfGroups` handles columns that SUBSTITUTE for one another — Employee
    // Email or Employee Name being the case that motivated it. A column named in
    // a group is exempt from its own Required check; instead the group as a whole
    // must contribute at least one column, and a group that contributes none is
    // reported as "Employee Email or Employee Name".
    public static (TabularHeaderMap? Map, IReadOnlyList<string> MissingLabels) Build(
        IReadOnlyList<string> headerRow,
        IReadOnlyList<TabularColumn> columns,
        IReadOnlyList<IReadOnlyList<string>>? anyOfGroups = null)
    {
        var aliasToKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            aliasToKey[Normalize(column.Key)] = column.Key;
            aliasToKey[Normalize(column.Label)] = column.Key;
            foreach (var alias in column.Aliases ?? [])
                aliasToKey[Normalize(alias)] = column.Key;
        }

        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < headerRow.Count; i++)
        {
            if (aliasToKey.TryGetValue(Normalize(headerRow[i]), out var key) && !byKey.ContainsKey(key))
                byKey[key] = i;
        }

        var groups = anyOfGroups ?? [];
        var inAGroup = groups.SelectMany(g => g).ToHashSet(StringComparer.Ordinal);

        var missing = columns
            .Where(c => c.Required && !inAGroup.Contains(c.Key) && !byKey.ContainsKey(c.Key))
            .Select(c => c.Label)
            .ToList();

        foreach (var group in groups)
        {
            if (group.Any(byKey.ContainsKey)) continue;

            var labels = group
                .Select(key => columns.FirstOrDefault(c => c.Key == key)?.Label ?? key);
            missing.Add(string.Join(" or ", labels));
        }

        return missing.Count > 0 ? (null, missing) : (new TabularHeaderMap(byKey), []);
    }

    public bool Has(string key) => _byKey.ContainsKey(key);

    // Missing column or short row → empty string. Importers treat empty as
    // "not supplied" and decide for themselves whether that's an error, so this
    // never has to distinguish absent from blank.
    public string Cell(IReadOnlyList<string> row, string key)
    {
        if (!_byKey.TryGetValue(key, out var index) || index >= row.Count) return string.Empty;
        return row[index];
    }

    // "* Employee Email " and "employee_email" both normalise to "employeeemail".
    private static string Normalize(string header)
    {
        var trimmed = header.Trim().TrimStart('*').Trim();
        return new string(trimmed.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}

// Cell → typed value. Every importer parses through these so a date written
// one way in a claims file means the same thing in a leave file.
public static class TabularCell
{
    // Accepts yyyy-MM-dd (canonical, what our own exports and templates emit)
    // plus the formats Excel likes to hand back after a round-trip.
    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "yyyy/MM/dd", "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "MMM d yyyy", "d MMM yyyy",
    ];

    private static readonly string[] DateTimeFormats =
    [
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-dd hh:mm tt", "dd/MM/yyyy HH:mm", "d/M/yyyy HH:mm",
    ];

    private static readonly string[] TimeFormats =
    [
        "HH:mm", "H:mm", "HH:mm:ss", "hh:mm tt", "h:mm tt",
    ];

    public static bool IsBlank(string value) => value.Trim().Length == 0;

    public static DateTime? Date(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;

        // Ambiguity is real here: 03/04/2026 is March 4th to one admin and April
        // 3rd to another. Fixed formats in a deliberate order (ISO first, then
        // day-first) beat DateTime.Parse's culture guess, which would silently
        // depend on the SERVER's locale.
        if (DateTime.TryParseExact(trimmed, DateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exact))
            return exact.Date;

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var loose) ? loose.Date : null;
    }

    // A full instant. Used for clock-in/out columns, which may arrive either as
    // "2026-01-05 09:03" or as a bare "09:03" alongside a separate date column.
    public static DateTime? Instant(string value, DateTime? onDate = null)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return null;

        if (DateTime.TryParseExact(trimmed, DateTimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var full))
            return DateTime.SpecifyKind(full, DateTimeKind.Utc);

        if (onDate is { } day &&
            DateTime.TryParseExact(trimmed, TimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timeOnly))
            return DateTime.SpecifyKind(day.Date.Add(timeOnly.TimeOfDay), DateTimeKind.Utc);

        return DateTime.TryParse(trimmed, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var loose)
            ? DateTime.SpecifyKind(loose, DateTimeKind.Utc)
            : null;
    }

    public static decimal? Money(string value)
    {
        // Strip currency symbols and thousands separators — "RM 1,250.00" is a
        // perfectly ordinary thing to find in a file exported from accounting.
        var cleaned = new string(value.Where(c => char.IsDigit(c) || c is '.' or '-').ToArray());
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
    }

    public static double? Number(string value) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;

    public static int? Integer(string value) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : null;

    // Case- and punctuation-insensitive enum match, so "on time", "ON_TIME" and
    // "OnTime" all land on the same member.
    public static TEnum? Enum<TEnum>(string value) where TEnum : struct, Enum
    {
        var normalized = new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        if (normalized.Length == 0) return null;

        foreach (var candidate in System.Enum.GetValues<TEnum>())
        {
            var name = new string(candidate.ToString()!
                .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
            if (name == normalized) return candidate;
        }

        return null;
    }

    public static string? Text(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    // Truncates to a column's [MaxLength] rather than letting MySQL reject the
    // whole batch over one over-long free-text note.
    public static string? Text(string value, int maxLength)
    {
        var text = Text(value);
        return text is null || text.Length <= maxLength ? text : text[..maxLength];
    }
}

public static class TabularTemplate
{
    // The downloadable template: a header row (required columns marked `*`) plus
    // one example row showing the expected shape of every value. The example row
    // is data as far as the parser is concerned, so it's labelled in the first
    // column and admins are told to replace it.
    public static TabularSheet Build(string sheetName, IReadOnlyList<TabularColumn> columns)
    {
        var headers = columns.Select(c => c.Required ? $"*{c.Label}" : c.Label).ToList();
        var sheet = new TabularSheet(sheetName, headers);
        sheet.AddRow(columns.Select(c => (string?)c.Example));
        return sheet;
    }

    // True when a row is the untouched example row from Build() above.
    //
    // Admins fill the template in BELOW the example rather than overwriting it,
    // so without this every template-based import would create one nonsense
    // record. Matching on the exact example values (rather than a magic marker
    // string) means a genuinely filled-in row can never be mistaken for it.
    // A real row that happens to equal the example verbatim is reported as
    // skipped, not silently dropped.
    public static bool IsExampleRow(
        TabularHeaderMap map, IReadOnlyList<string> row, IReadOnlyList<TabularColumn> columns)
    {
        var matched = 0;

        // Cells are fetched BY KEY, not by position, so a template whose columns
        // were reordered or partly deleted is still recognised.
        foreach (var column in columns)
        {
            var cell = map.Cell(row, column.Key).Trim();
            if (cell.Length == 0) continue;
            if (!string.Equals(cell, column.Example.Trim(), StringComparison.OrdinalIgnoreCase))
                return false;
            matched++;
        }

        // At least two columns must match, so a near-empty row isn't swallowed.
        return matched >= 2;
    }
}
