using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Documents;
using AltomateHR.Api.Modules.Fah.Entities;
using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Fah;

// What FAH can read from an attached file, and the limits that keep a chat
// from getting expensive: every file in a chat is read again (from the
// prompt cache, cheaply) on every later message.
//
//   images      PNG, JPEG, GIF, WEBP — the model looks at them directly
//   PDF         text and page images, read by the model directly
//   .xlsx/.csv  read into text here: one block per sheet, rows as CSV
//   .docx       read into text here (headings, lists and table rows kept)
//
// Pure: no I/O beyond the bytes handed in. A file that can't be read is a
// refusal with a reason the admin can act on, never an exception — and it is
// refused HERE, at upload, because a file the model can't take would break
// every later message in the chat it went into.
public static partial class FahFiles
{
    public const int MaxPerMessage = 5;
    public const int MaxPerChat = 10;

    public const long MaxImageBytes = 5 * 1024 * 1024;
    public const long MaxPdfBytes = 8 * 1024 * 1024;
    public const long MaxSheetBytes = 5 * 1024 * 1024;
    public const int MaxPdfPages = 30;
    // All the PDFs in one chat together.
    public const int MaxPdfPagesPerChat = 60;
    // The model's limit on an image side, in pixels.
    public const int MaxImageSide = 8000;
    // Per spreadsheet or Word file, about 8,000 tokens.
    public const int MaxTextChars = 30_000;
    // A spreadsheet's rows kept whole for an employee import (FahImportService):
    // the model only reads the first MaxTextChars, but an import takes them all.
    public const int MaxTableRows = 5_000;

    // How long the model provider keeps an image or PDF. A chat with one can't
    // be continued after this; FahService refuses a little before it.
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(30);

    public const string Kinds = "images (PNG, JPG, GIF, WEBP), PDF, Excel (.xlsx), CSV or Word (.docx)";

    public sealed record Prepared(
        FahAttachmentKind Kind, string MediaType, string FileName, string? Text, int Pages, string? Note,
        IReadOnlyList<FahSheet>? Table = null);

    // A spreadsheet's sheet as rows of cells, the header row included.
    public sealed record FahSheet(string Name, IReadOnlyList<IReadOnlyList<string>> Rows);

    public sealed record Outcome(Prepared? File, string? Error)
    {
        public static Outcome Refused(string error) => new(null, error);
    }

    public static Outcome Prepare(string? fileName, byte[] content)
    {
        var name = CleanName(fileName);
        if (content.Length == 0) return Outcome.Refused($"{name} is empty.");

        if (ImageMediaType(content) is { } imageType) return PrepareImage(name, imageType, content);
        if (content.AsSpan().StartsWith("%PDF-"u8)) return PreparePdf(name, content);

        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".xlsx" or ".xlsm" => PrepareWorkbook(name, content),
            ".csv" => PrepareCsv(name, content),
            ".docx" => PrepareWord(name, content),
            ".xls" => Outcome.Refused($"{name} is an old Excel file. Open it in Excel, save it as .xlsx, and attach that."),
            ".doc" => Outcome.Refused($"{name} is an old Word file. Open it in Word, save it as .docx, and attach that."),
            ".heic" or ".heif" => Outcome.Refused($"{name} is an iPhone HEIC photo. Attach it as a JPG or PNG instead."),
            _ => Outcome.Refused($"FAH can't read {name}. Attach {Kinds}."),
        };
    }

    // ─── Images ──────────────────────────────────────────────────────────

    private static Outcome PrepareImage(string name, string mediaType, byte[] content)
    {
        if (content.Length > MaxImageBytes) return Outcome.Refused($"{name} is larger than 5 MB.");
        if (ImageSize(content) is not var (width, height) || width <= 0 || height <= 0)
            return Outcome.Refused($"{name} couldn't be read as an image.");
        if (width > MaxImageSide || height > MaxImageSide)
            return Outcome.Refused($"{name} is too large ({width}×{height}). Attach a smaller copy.");
        return new Outcome(new Prepared(FahAttachmentKind.Image, mediaType, name, null, 0, null), null);
    }

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static string? ImageMediaType(byte[] b)
    {
        var s = b.AsSpan();
        if (s.StartsWith(PngSignature)) return "image/png";
        if (s.StartsWith(JpegSignature)) return "image/jpeg";
        if (s.StartsWith("GIF87a"u8) || s.StartsWith("GIF89a"u8)) return "image/gif";
        if (s.Length >= 12 && s.StartsWith("RIFF"u8) && s[8..12].SequenceEqual("WEBP"u8)) return "image/webp";
        return null;
    }

    // Width and height from the header, or null when it can't be found.
    public static (int Width, int Height)? ImageSize(byte[] b)
    {
        var s = b.AsSpan();
        try
        {
            if (s.StartsWith(PngSignature))
                return (BinaryPrimitives.ReadInt32BigEndian(s[16..]), BinaryPrimitives.ReadInt32BigEndian(s[20..]));

            if (s.StartsWith("GIF"u8))
                return (BinaryPrimitives.ReadUInt16LittleEndian(s[6..]), BinaryPrimitives.ReadUInt16LittleEndian(s[8..]));

            if (s.StartsWith("RIFF"u8))
            {
                var chunk = s[12..16];
                if (chunk.SequenceEqual("VP8X"u8))
                    return (1 + (s[24] | s[25] << 8 | s[26] << 16), 1 + (s[27] | s[28] << 8 | s[29] << 16));
                if (chunk.SequenceEqual("VP8L"u8))
                {
                    var bits = BinaryPrimitives.ReadUInt32LittleEndian(s[21..]);
                    return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
                }
                if (chunk.SequenceEqual("VP8 "u8))
                    return (BinaryPrimitives.ReadUInt16LittleEndian(s[26..]) & 0x3FFF,
                            BinaryPrimitives.ReadUInt16LittleEndian(s[28..]) & 0x3FFF);
                return null;
            }

            if (s.StartsWith(JpegSignature))
            {
                // Walk the markers to the frame header (SOFn).
                var i = 2;
                while (i + 9 < s.Length)
                {
                    if (s[i] != 0xFF) return null;
                    var marker = s[i + 1];
                    if (marker == 0xFF) { i++; continue; }
                    if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; }
                    var length = BinaryPrimitives.ReadUInt16BigEndian(s[(i + 2)..]);
                    if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                        return (BinaryPrimitives.ReadUInt16BigEndian(s[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(s[(i + 5)..]));
                    i += 2 + length;
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // A header cut short.
        }
        return null;
    }

    // ─── PDF ─────────────────────────────────────────────────────────────

    private static Outcome PreparePdf(string name, byte[] content)
    {
        if (content.Length > MaxPdfBytes) return Outcome.Refused($"{name} is larger than 8 MB.");

        var raw = Encoding.Latin1.GetString(content);
        if (raw.Contains("/Encrypt", StringComparison.Ordinal))
            return Outcome.Refused($"{name} is password-protected. Save a copy without the password and attach that.");

        // Page objects counted in the raw file. A PDF that packs its objects
        // into compressed streams shows none — then the count is unknown (0)
        // and the size limit is the only guard.
        var pages = PdfPage().Count(raw);
        if (pages > MaxPdfPages)
            return Outcome.Refused($"{name} has {pages} pages; FAH reads up to {MaxPdfPages}. Attach just the pages you need.");

        return new Outcome(new Prepared(FahAttachmentKind.Pdf, "application/pdf", name, null, pages, null), null);
    }

    [GeneratedRegex(@"/Type\s*/Page(?![A-Za-z])")]
    private static partial Regex PdfPage();

    // ─── Spreadsheets ────────────────────────────────────────────────────

    private static Outcome PrepareWorkbook(string name, byte[] content)
    {
        if (content.Length > MaxSheetBytes) return Outcome.Refused($"{name} is larger than 5 MB.");
        var unreadable = $"{name} couldn't be read. Open it in Excel, save it as .xlsx, and try again.";
        if (!ZipLooksSafe(content)) return Outcome.Refused(unreadable);

        try
        {
            using var workbook = new XLWorkbook(new MemoryStream(content, writable: false));
            var text = new TextLimit(MaxTextChars);
            var sheetsRead = 0;
            foreach (var sheet in workbook.Worksheets)
            {
                var used = sheet.RangeUsed();
                if (used is null) continue;
                var rows = used.RowsUsed().ToList();
                var hidden = sheet.Visibility == XLWorksheetVisibility.Visible ? "" : ", hidden sheet";
                if (!text.Add($"## Sheet \"{sheet.Name}\" ({rows.Count} rows{hidden})\n")) break;
                sheetsRead++;
                foreach (var row in rows)
                {
                    var cells = row.Cells(1, used.ColumnCount()).Select(CellText);
                    if (!text.AddRow(string.Join(",", cells.Select(Csv)), sheet.Name, rows.Count)) break;
                }
                if (text.Full) break;
                text.Add("\n");
            }

            if (sheetsRead == 0) return Outcome.Refused($"{name} has no data in it.");
            return new Outcome(new Prepared(
                FahAttachmentKind.Text, "text/plain", name, text.ToString().TrimEnd(), 0, text.Note,
                Table(content, TabularFormat.Xlsx)), null);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or InvalidOperationException
                                      or ArgumentException or FormatException or NotSupportedException
                                      or System.Xml.XmlException or OverflowException)
        {
            return Outcome.Refused(unreadable);
        }
    }

    private static string CellText(IXLCell cell)
    {
        try
        {
            return cell.GetFormattedString();
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or ArgumentException)
        {
            return cell.CachedValue.ToString() ?? string.Empty;
        }
    }

    private static string Csv(string value)
    {
        var v = value.Replace("\r\n", "\n").Replace('\r', '\n');
        return v.IndexOfAny([',', '"', '\n']) < 0 ? v : $"\"{v.Replace("\"", "\"\"")}\"";
    }

    private static Outcome PrepareCsv(string name, byte[] content)
    {
        if (content.Length > MaxSheetBytes) return Outcome.Refused($"{name} is larger than 5 MB.");
        var raw = Decode(content);
        if (raw.Contains('\0')) return Outcome.Refused($"{name} doesn't look like a CSV file.");

        var lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            .Where(l => l.Trim(',', ' ', '\t').Length > 0)
            .ToList();
        if (lines.Count == 0) return Outcome.Refused($"{name} has no data in it.");

        var text = new TextLimit(MaxTextChars);
        foreach (var line in lines)
            if (!text.AddRow(line, null, lines.Count)) break;
        return new Outcome(new Prepared(
            FahAttachmentKind.Text, "text/plain", name, text.ToString().TrimEnd(), 0, text.Note,
            Table(content, TabularFormat.Csv)), null);
    }

    // Read the way the employee import reads a file, so a cell means the same
    // to both. Empty sheets are left out; rows past MaxTableRows are dropped.
    private static IReadOnlyList<FahSheet> Table(byte[] content, TabularFormat format) =>
        [.. TabularReader.ReadAllSheets(content, format)
            .Where(s => s.Rows.Any(r => r.Any(c => !string.IsNullOrWhiteSpace(c))))
            .Select(s => new FahSheet(s.Name, [.. s.Rows.Take(MaxTableRows)]))];

    // UTF-8 when it is (Excel adds a BOM), otherwise the Windows default
    // Excel saves CSVs in — read as Latin-1, which keeps every byte.
    private static string Decode(byte[] content)
    {
        try
        {
            using var reader = new StreamReader(new MemoryStream(content),
                new UTF8Encoding(false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    // ─── Word ────────────────────────────────────────────────────────────

    private static Outcome PrepareWord(string name, byte[] content)
    {
        if (content.Length > MaxSheetBytes) return Outcome.Refused($"{name} is larger than 5 MB.");
        var word = DocxTemplateImport.Convert(name, content);
        if (!word.Ok) return Outcome.Refused($"{name}: {word.Error}");

        var body = word.Body;
        string? note = null;
        if (body.Length > MaxTextChars)
        {
            var cut = body.LastIndexOf('\n', MaxTextChars);
            body = body[..(cut > 0 ? cut : MaxTextChars)];
            note = $"Only the first {body.Length:N0} characters were read.";
        }
        return new Outcome(new Prepared(FahAttachmentKind.Text, "text/plain", name, body, 0, note), null);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    // A name safe to show and to put in the chat: no path, no control
    // characters, no brackets (they mark attachments in the stored chat).
    public static string CleanName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/'));
        var clean = new string(name.Select(c => char.IsControl(c) ? ' ' : c is '[' ? '(' : c is ']' ? ')' : c).ToArray()).Trim();
        if (clean.Length == 0) return "file";
        if (clean.Length <= 120) return clean;
        var extension = Path.GetExtension(clean);
        return extension.Length is > 0 and <= 10 ? clean[..(120 - extension.Length)] + extension : clean[..120];
    }

    // An .xlsx is a zip: refuse one that would expand to something absurd
    // before ClosedXML opens it.
    private static bool ZipLooksSafe(byte[] content)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > 2000) return false;
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (entry.Length < 0 || total > 64L * 1024 * 1024) return false;
            }
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    // Collects text up to a limit, cutting at a whole row, and says what was left out.
    private sealed class TextLimit(int max)
    {
        private readonly StringBuilder _text = new();
        private int _rows;

        public bool Full { get; private set; }
        public string? Note { get; private set; }

        public bool Add(string text)
        {
            if (Full) return false;
            if (_text.Length + text.Length > max)
            {
                Full = true;
                Note ??= "Only part of the file was read: it is longer than FAH reads.";
                return false;
            }
            _text.Append(text);
            return true;
        }

        public bool AddRow(string row, string? sheet, int total)
        {
            if (Add(row + "\n"))
            {
                _rows++;
                return true;
            }
            Note = sheet is null
                ? $"Only the first {_rows:N0} of {total:N0} rows were read."
                : $"Only the first rows were read (sheet \"{sheet}\" has {total:N0}).";
            return false;
        }

        public override string ToString() => _text.ToString();
    }
}
