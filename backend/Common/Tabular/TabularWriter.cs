using System.Text;
using ClosedXML.Excel;

namespace AltomateHR.Api.Common.Tabular;

// Renders a TabularSheet as CSV, XLSX or PDF bytes. The ONLY place any of the
// three is produced, so "how does an export look" is one decision, not one per
// module — and a module that describes its report once gets all three.
public static class TabularWriter
{
    public static byte[] Write(TabularSheet sheet, TabularFormat format, TabularPdfHeader? pdfHeader = null) =>
        Write([sheet], format, pdfHeader);

    // Multi-sheet: XLSX gets one worksheet per sheet; PDF gets a page sequence
    // each; CSV can't express either, so the sheets are stacked with a labelled
    // separator row between them. Callers that need a re-importable CSV should
    // export one sheet at a time.
    public static byte[] Write(
        IReadOnlyList<TabularSheet> sheets, TabularFormat format, TabularPdfHeader? pdfHeader = null) =>
        format switch
        {
            TabularFormat.Xlsx => WriteXlsx(sheets),
            // A caller that asks for PDF without a header gets a neutral one
            // rather than an exception — a missing masthead shouldn't cost
            // somebody their download.
            TabularFormat.Pdf => TabularPdfRenderer.Render(
                sheets, pdfHeader ?? new TabularPdfHeader("Report", sheets.FirstOrDefault()?.Name ?? "Export")),
            _ => WriteCsv(sheets),
        };

    private static byte[] WriteCsv(IReadOnlyList<TabularSheet> sheets)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < sheets.Count; i++)
        {
            var sheet = sheets[i];

            // Only label sheets when there's more than one — a single-sheet CSV
            // must start with its header row so it can be re-imported as-is.
            if (sheets.Count > 1)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(Cell($"# {sheet.Name}")).Append('\n');
            }

            sb.AppendJoin(',', sheet.Headers.Select(Cell)).Append('\n');
            foreach (var row in sheet.Rows)
                sb.AppendJoin(',', row.Select(Cell)).Append('\n');

            if (sheet.TotalsRow is { } totals)
                sb.AppendJoin(',', totals.Select(Cell)).Append('\n');
        }

        // UTF-8 BOM so Excel shows non-ASCII names instead of mojibake.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static byte[] WriteXlsx(IReadOnlyList<TabularSheet> sheets)
    {
        using var workbook = new XLWorkbook();

        foreach (var sheet in sheets)
        {
            var worksheet = workbook.Worksheets.Add(UniqueName(workbook, sheet.Name));

            for (var c = 0; c < sheet.Headers.Count; c++)
                worksheet.Cell(1, c + 1).Value = sheet.Headers[c];

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.SetAutoFilter();
            worksheet.SheetView.FreezeRows(1);   // header stays put while HR scrolls

            var r = 2;
            foreach (var row in sheet.Rows)
            {
                for (var c = 0; c < row.Count; c++)
                {
                    // Written as text on purpose. These cells already carry the
                    // canonical formatting (see TabularSheet's formatters), and
                    // letting Excel re-interpret them turns "2026-01-05" into
                    // whatever the reader's locale prefers — which then fails to
                    // re-import. SetValue(string) skips Excel's type inference.
                    worksheet.Cell(r, c + 1).SetValue(row[c]);
                }
                r++;
            }

            if (sheet.TotalsRow is { } totals)
            {
                for (var c = 0; c < totals.Count; c++)
                    worksheet.Cell(r, c + 1).SetValue(totals[c]);
                worksheet.Row(r).Style.Font.Bold = true;
            }

            worksheet.Columns().AdjustToContents(1, 200, 8, 60);
        }

        // Excel refuses to open a workbook with zero sheets.
        if (!workbook.Worksheets.Any()) workbook.Worksheets.Add("Sheet1");

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // Two exports could legitimately name their sheets the same thing; ClosedXML
    // throws on a duplicate, so suffix instead of failing the download.
    private static string UniqueName(XLWorkbook workbook, string name)
    {
        if (!workbook.Worksheets.Any(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase)))
            return name;

        for (var n = 2; n < 100; n++)
        {
            var candidate = $"{name} ({n})";
            if (candidate.Length > 31) candidate = $"{name[..Math.Min(name.Length, 27)]} ({n})";
            if (!workbook.Worksheets.Any(w => string.Equals(w.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return Guid.NewGuid().ToString()[..8];
    }

    // RFC 4180: quote when the value holds a comma, quote or newline, and escape
    // embedded quotes by doubling them.
    private static string Cell(string value)
    {
        if (!value.Any(c => c is ',' or '"' or '\n' or '\r')) return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
