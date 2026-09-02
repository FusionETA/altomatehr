using System.Text;
using ClosedXML.Excel;

namespace AltomateHR.Api.Common.Tabular;

// Parses an uploaded CSV or XLSX into raw string rows (header row included).
// The ONLY place either format is read, so every importer agrees on quoting,
// blank-row handling and cell stringification.
//
// Deliberately dumb: no header mapping, no validation, no type coercion. That's
// TabularHeaderMap's and each importer's job — this layer only turns bytes into
// a rectangle of strings.
public static class TabularReader
{
    // A pathological upload shouldn't be able to exhaust memory or wedge a
    // request thread; importers are for migrations, not bulk ingestion.
    public const int MaxRows = 20_000;

    public static IReadOnlyList<IReadOnlyList<string>> Read(byte[] content, TabularFormat format)
    {
        // Defensive: the import endpoints already refuse a .pdf upload, but a
        // future caller passing Pdf here should get a clear refusal rather than
        // the CSV parser turning binary into "rows".
        if (!format.IsImportable())
            throw new InvalidDataException("PDF is an export format only — upload a .csv or .xlsx file.");

        var rows = format == TabularFormat.Xlsx ? ReadXlsx(content) : ReadCsv(content);

        // Wholly blank rows are an artifact of how spreadsheets save, never data.
        // Trailing whitespace in a cell is almost always accidental too.
        return rows
            .Select(r => (IReadOnlyList<string>)r.Select(c => c.Trim()).ToList())
            .Where(r => r.Any(c => c.Length > 0))
            .Take(MaxRows)
            .ToList();
    }

    private static List<List<string>> ReadXlsx(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(stream);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("That file isn't a readable .xlsx workbook.", ex);
        }

        using (workbook)
        {
            var worksheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidDataException("The workbook has no sheets.");

            var used = worksheet.RangeUsed();
            if (used is null) return [];

            var lastColumn = used.LastColumn().ColumnNumber();
            var rows = new List<List<string>>();

            foreach (var row in used.RowsUsed())
            {
                var cells = new List<string>(lastColumn);
                for (var c = used.FirstColumn().ColumnNumber(); c <= lastColumn; c++)
                {
                    // GetFormattedString() honours the cell's number format, so a
                    // date typed into Excel comes back the way the admin saw it
                    // rather than as an OLE serial number.
                    cells.Add(row.Cell(c).GetFormattedString());
                }
                rows.Add(cells);
            }

            return rows;
        }
    }

    // RFC 4180 with the usual real-world tolerances: CRLF or LF line endings, a
    // stray UTF-8 BOM, and quotes appearing mid-field.
    private static List<List<string>> ReadCsv(byte[] content)
    {
        var text = Encoding.UTF8.GetString(content);
        if (text.StartsWith('﻿')) text = text[1..];

        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch != '"')
                {
                    cell.Append(ch);
                }
                else if (i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');   // "" → a literal quote
                    i++;
                }
                else
                {
                    inQuotes = false;
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;              // handled by the \n that follows
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row);
                    row = [];
                    break;
                default:
                    cell.Append(ch);
                    break;
            }
        }

        // A file with no trailing newline still has one last row to flush.
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
