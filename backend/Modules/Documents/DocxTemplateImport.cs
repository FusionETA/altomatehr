using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AltomateHR.Api.Modules.Documents;

// Turns an uploaded file into template markup (see LetterMarkup), in memory —
// the upload itself is never stored. The admin reviews the result in the
// editor and saves it as an ordinary template.
//
//   .docx       converted (below)
//   .txt / .md  taken as markup as-is, line endings normalised
//
// What a Word file keeps:
//   paragraphs → paragraphs; a line break inside one stays a line break
//   Title / Heading 1 → "# ", Heading 2 and lower → "## "
//   bold runs → **bold** (adjacent runs merged; never across a line break)
//   list items (bulleted or numbered) → "- " bullets
//   tables → one paragraph per row, cells joined by " | "
//   {{placeholders}} — reassembled when Word split one across several runs
//   Word mail-merge fields (MERGEFIELD Name) → {{Name}}
// What it drops, counted in the warnings: images, text boxes, headers and
// footers, footnotes. Italic, underline, fonts, colours, alignment and
// indentation are not carried over (the PDF has its own letter styling).
public static class DocxTemplateImport
{
    public const long MaxBytes = 5 * 1024 * 1024;

    // Zip-bomb guards: what the package may expand to, and how big one XML
    // part may be once read.
    private const long MaxUncompressedBytes = 64L * 1024 * 1024;
    private const int MaxEntries = 2000;
    private const long MaxCharactersInPart = 20_000_000;

    public const string UnreadableWord = "This Word file couldn't be read. Open it in Word, save it as .docx, and try again.";

    public sealed record Outcome(
        bool Ok,
        string? Error,
        string SuggestedName,
        string Body,
        IReadOnlyList<string> Warnings)
    {
        public static Outcome Refused(string error) => new(false, error, "", "", []);
    }

    // Any {{…}}, valid key or not — "{{Employee Name}}" from a Word document
    // must be found so the editor can offer to map it.
    private static readonly Regex AnyPlaceholder = new(@"\{\{\s*([^{}\r\n]*?)\s*\}\}", RegexOptions.Compiled);

    private static readonly Regex MergeFieldInstruction =
        new(@"^\s*MERGEFIELD\s+(?:""([^""]+)""|(\S+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// The placeholders the text mentions whose key isn't a registry field or
    /// a valid {{input.xxx}} — in first-use order, as written (trimmed).
    /// </summary>
    public static IReadOnlyList<string> UnknownPlaceholders(string body) =>
        AnyPlaceholder.Matches(body)
            .Select(m => m.Groups[1].Value.Trim())
            .Where(k => !MergeFields.IsKnown(k))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static Outcome Convert(string? fileName, byte[] content)
    {
        if (content.Length == 0) return Outcome.Refused("The file is empty.");
        if (content.Length > MaxBytes) return Outcome.Refused("The file is larger than 5 MB.");

        var name = Path.GetFileName(fileName ?? "") ?? "";
        var extension = Path.GetExtension(name).ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(name);

        return extension switch
        {
            ".docx" => ConvertDocx(baseName, content),
            ".txt" or ".md" or ".markdown" => ConvertText(baseName, content),
            ".doc" => Outcome.Refused("Old .doc files can't be read. Open it in Word, save it as .docx, and upload that."),
            _ => Outcome.Refused("Upload a Word (.docx), .txt or .md file."),
        };
    }

    // ─── Text / markdown ─────────────────────────────────────────────────

    private static Outcome ConvertText(string baseName, byte[] content)
    {
        string text;
        try
        {
            using var reader = new StreamReader(new MemoryStream(content),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            return Outcome.Refused("This text file isn't UTF-8. Save it as UTF-8 text and try again.");
        }

        if (text.Contains('\0')) return Outcome.Refused("This doesn't look like a text file.");

        var body = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');
        if (body.Trim().Length == 0) return Outcome.Refused("No text was found in this file.");

        var warnings = new List<string>();
        AddLengthWarning(body, warnings);
        return new Outcome(true, null, SuggestName(body, baseName), body, warnings);
    }

    // ─── Word ────────────────────────────────────────────────────────────

    private static Outcome ConvertDocx(string baseName, byte[] content)
    {
        if (!PackageLooksSafe(content, out var refusal)) return Outcome.Refused(refusal);

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var doc = WordprocessingDocument.Open(stream, false, new OpenSettings
            {
                MaxCharactersInPart = MaxCharactersInPart,
            });

            var main = doc.MainDocumentPart;
            var body = main?.Document?.Body;
            if (main is null || body is null) return Outcome.Refused(UnreadableWord);

            var converter = new Converter(main);
            var markup = converter.Run(body);
            if (markup.Trim().Length == 0) return Outcome.Refused("No text was found in this Word file.");

            var warnings = converter.Warnings(main);
            AddLengthWarning(markup, warnings);
            return new Outcome(true, null, SuggestName(markup, baseName), markup, warnings);
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or FileFormatException
                                      or InvalidOperationException or System.Xml.XmlException
                                      or IOException or ArgumentException or NotSupportedException)
        {
            return Outcome.Refused(UnreadableWord);
        }
    }

    // Reads only the zip's central directory — cheap — and refuses a package
    // that claims to expand far beyond any real letter.
    private static bool PackageLooksSafe(byte[] content, out string refusal)
    {
        refusal = UnreadableWord;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content, writable: false), ZipArchiveMode.Read);
            if (zip.Entries.Count > MaxEntries) return false;
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (entry.Length < 0 || total > MaxUncompressedBytes)
                {
                    refusal = "This Word file is too large to import.";
                    return false;
                }
            }
            return zip.GetEntry("word/document.xml") is not null
                   || zip.Entries.Any(e => e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or ArgumentException)
        {
            return false;
        }
    }

    // One pass over the document body. Holds the counters for the warnings.
    private sealed class Converter
    {
        private readonly Styles? _styles;
        private readonly Numbering? _numbering;
        private readonly StringBuilder _out = new();
        private bool _lastWasBullet;

        private int _images;
        private int _textBoxes;
        private int _tables;
        private int _numberedItems;
        private int _mergeFields;

        public Converter(MainDocumentPart main)
        {
            _styles = main.StyleDefinitionsPart?.Styles;
            _numbering = main.NumberingDefinitionsPart?.Numbering;
        }

        public string Run(Body body)
        {
            CountDropped(body);
            foreach (var child in body.ChildElements) Block(child);
            return _out.ToString().TrimEnd('\n');
        }

        public List<string> Warnings(MainDocumentPart main)
        {
            var warnings = new List<string>();
            if (_images > 0)
                warnings.Add($"{_images} image{Plural(_images)} {(_images == 1 ? "was" : "were")} skipped — the letterhead comes from Company Info.");
            if (_textBoxes > 0)
                warnings.Add($"{_textBoxes} text box{(_textBoxes == 1 ? "" : "es")} {(_textBoxes == 1 ? "was" : "were")} skipped — copy any text you need from them by hand.");
            if (_tables > 0)
                warnings.Add($"{_tables} table{Plural(_tables)} {(_tables == 1 ? "was" : "were")} converted to text: one paragraph per row, cells separated by \" | \".");
            if (_numberedItems > 0)
                warnings.Add($"{_numberedItems} numbered list item{Plural(_numberedItems)} became bullet points.");
            if (_mergeFields > 0)
                warnings.Add($"{_mergeFields} Word mail-merge field{Plural(_mergeFields)} became {{{{…}}}} placeholders — map each one below.");

            var headerFooter =
                main.HeaderParts.Any(h => HasText(h.Header)) || main.FooterParts.Any(f => HasText(f.Footer));
            if (headerFooter)
                warnings.Add("The header and footer were skipped — the PDF prints the company letterhead.");
            if (main.FootnotesPart?.Footnotes is { } notes
                && notes.Elements<Footnote>().Any(n => (n.Id?.Value ?? 0) > 0 && HasText(n)))
                warnings.Add("Footnotes were skipped.");
            return warnings;
        }

        private static bool HasText(OpenXmlElement? e) =>
            e is not null && e.Descendants<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text));

        private static string Plural(int n) => n == 1 ? "" : "s";

        // ─── Dropped content ───────────────────────────────────────────

        private void CountDropped(Body body)
        {
            foreach (var box in body.Descendants<TextBoxContent>())
                if (!InFallback(box) && !box.Ancestors<TextBoxContent>().Any()) _textBoxes++;

            foreach (var drawing in body.Descendants<Drawing>())
                if (!InFallback(drawing) && !drawing.Descendants<TextBoxContent>().Any()
                    && !drawing.Ancestors<TextBoxContent>().Any()) _images++;

            foreach (var pict in body.Descendants<Picture>())
                if (!InFallback(pict) && !pict.Descendants<TextBoxContent>().Any()
                    && !pict.Ancestors<TextBoxContent>().Any()) _images++;

            foreach (var obj in body.Descendants<EmbeddedObject>())
                if (!InFallback(obj)) _images++;
        }

        // Word writes a modern drawing and a legacy fallback of the same
        // thing; only the first counts.
        private static bool InFallback(OpenXmlElement e) =>
            e.Ancestors().Any(a => a.LocalName == "Fallback");

        // ─── Blocks ────────────────────────────────────────────────────

        private void Block(OpenXmlElement element)
        {
            switch (element)
            {
                case Paragraph p:
                    ConvertParagraph(p);
                    break;
                case Table t:
                    _tables++;
                    ConvertTable(t);
                    break;
                case SectionProperties:
                    break;
                default:
                    // Content controls, custom XML: their content is ordinary
                    // paragraphs and tables.
                    foreach (var child in element.ChildElements)
                        if (child is Paragraph or Table or SdtBlock or SdtContentBlock or CustomXmlBlock)
                            Block(child);
                    break;
            }
        }

        private void ConvertParagraph(Paragraph p)
        {
            var text = Inline(p);
            var plain = Plain(text);
            if (plain.Trim().Length == 0) return;

            var heading = HeadingLevel(p);
            if (heading > 0)
            {
                EmitBlock((heading == 1 ? "# " : "## ") + OneLine(plain));
                return;
            }

            if (IsListItem(p, out var numbered))
            {
                if (numbered) _numberedItems++;
                if (!_lastWasBullet && _out.Length > 0) _out.Append('\n');
                _out.Append("- ").Append(OneLine(Markup(text))).Append('\n');
                _lastWasBullet = true;
                return;
            }

            EmitBlock(Markup(text));
        }

        private void ConvertTable(Table table)
        {
            foreach (var row in table.Elements<TableRow>())
            {
                var cells = row.Elements<TableCell>()
                    .Select(cell => OneLine(CellText(cell)))
                    .ToList();
                if (cells.All(c => c.Trim().Length == 0)) continue;
                EmitBlock(string.Join(" | ", cells.Select(c => c.Trim())));
            }
        }

        // A cell's paragraphs (and any nested table) as one run of text.
        private string CellText(TableCell cell)
        {
            var parts = new List<string>();
            foreach (var p in cell.Descendants<Paragraph>())
            {
                if (p.Ancestors<TextBoxContent>().Any()) continue;
                var line = Markup(Inline(p));
                if (line.Trim().Length > 0) parts.Add(OneLine(line));
            }
            return string.Join(" ", parts);
        }

        // A paragraph-level block, separated from the one before by a blank line.
        private void EmitBlock(string text)
        {
            if (_out.Length > 0) _out.Append('\n');
            _out.Append(text).Append('\n');
            _lastWasBullet = false;
        }

        private static string OneLine(string text) =>
            Regex.Replace(text.Replace('\n', ' '), " {2,}", " ").Trim();

        // ─── Styles ────────────────────────────────────────────────────

        // 1 for Title / Heading 1, 2 for any lower heading, 0 for body text.
        // Built-in style NAMES are English in every Word language ("heading 1"),
        // unlike their ids; an outline level counts too.
        private int HeadingLevel(Paragraph p)
        {
            var props = p.ParagraphProperties;
            if (props?.OutlineLevel?.Val?.Value is int direct && direct < 9) return direct == 0 ? 1 : 2;

            foreach (var style in StyleChain(props?.ParagraphStyleId?.Val?.Value))
            {
                var name = (style.StyleName?.Val?.Value ?? "").Trim().ToLowerInvariant();
                var id = (style.StyleId?.Value ?? "").ToLowerInvariant();
                if (name == "title" || id == "title") return 1;
                var m = Regex.Match(name, @"^heading\s*(\d)$");
                if (!m.Success) m = Regex.Match(id, @"^heading(\d)$");
                if (m.Success) return m.Groups[1].Value == "1" ? 1 : 2;
                if (style.StyleParagraphProperties?.OutlineLevel?.Val?.Value is int level && level < 9)
                    return level == 0 ? 1 : 2;
            }
            return 0;
        }

        private bool IsListItem(Paragraph p, out bool numbered)
        {
            numbered = false;
            var numPr = p.ParagraphProperties?.NumberingProperties;
            int? numId = numPr?.NumberingId?.Val?.Value;
            int level = numPr?.NumberingLevelReference?.Val?.Value ?? 0;

            if (numId is null)
            {
                foreach (var style in StyleChain(p.ParagraphProperties?.ParagraphStyleId?.Val?.Value))
                {
                    var styleNum = style.StyleParagraphProperties?.NumberingProperties;
                    if (styleNum?.NumberingId?.Val?.Value is int id)
                    {
                        numId = id;
                        level = styleNum.NumberingLevelReference?.Val?.Value ?? 0;
                        break;
                    }
                }
            }

            if (numId is null or 0) return false;
            numbered = IsNumberedFormat(numId.Value, level);
            return true;
        }

        private bool IsNumberedFormat(int numId, int level)
        {
            if (_numbering is null) return false;
            var instance = _numbering.Elements<NumberingInstance>()
                .FirstOrDefault(n => n.NumberID?.Value == numId);
            var abstractId = instance?.AbstractNumId?.Val?.Value;
            if (abstractId is null) return false;
            var abstractNum = _numbering.Elements<AbstractNum>()
                .FirstOrDefault(a => a.AbstractNumberId?.Value == abstractId);
            var lvl = abstractNum?.Elements<Level>().FirstOrDefault(l => l.LevelIndex?.Value == level);
            var format = lvl?.NumberingFormat?.Val;
            return format is not null && format.Value != NumberFormatValues.Bullet
                   && format.Value != NumberFormatValues.None;
        }

        // The style and what it's based on, nearest first (cycle-safe).
        private IEnumerable<Style> StyleChain(string? styleId)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (_styles is not null && styleId is not null && seen.Add(styleId) && seen.Count < 12)
            {
                var style = _styles.Elements<Style>().FirstOrDefault(s => s.StyleId?.Value == styleId);
                if (style is null) yield break;
                yield return style;
                styleId = style.BasedOn?.Val?.Value;
            }
        }

        private bool IsBold(Run run)
        {
            var b = run.RunProperties?.Bold;
            if (b is not null) return b.Val is null || b.Val.Value;

            foreach (var style in StyleChain(run.RunProperties?.RunStyle?.Val?.Value))
            {
                var sb = style.StyleRunProperties?.Bold;
                if (sb is not null) return sb.Val is null || sb.Val.Value;
            }
            return false;
        }

        // ─── Inline text ───────────────────────────────────────────────

        // One character of paragraph text and whether it's bold.
        private readonly record struct Ch(char C, bool Bold);

        // The paragraph's text, char by char: run texts concatenated FIRST, so
        // a placeholder Word split over several runs is whole again.
        private List<Ch> Inline(Paragraph p)
        {
            var chars = new List<Ch>();
            var fieldDepth = 0;            // inside a complex field (fldChar begin … end)
            var inResult = false;          // past its "separate"
            var instruction = new StringBuilder();
            var suppressResult = false;    // a MERGEFIELD we've already written as {{…}}
            var simpleFieldsDone = new HashSet<SimpleField>();

            void Append(string s, bool bold)
            {
                foreach (var c in s) chars.Add(new Ch(c, bold));
            }

            foreach (var run in p.Descendants<Run>())
            {
                if (run.Ancestors<TextBoxContent>().Any() || InFallback(run)) continue;

                // <w:fldSimple w:instr="MERGEFIELD Name">«Name»</w:fldSimple>
                var simple = run.Ancestors<SimpleField>().FirstOrDefault();
                if (simple is not null && MergeFieldName(simple.Instruction?.Value) is { } simpleName)
                {
                    if (simpleFieldsDone.Add(simple))
                    {
                        _mergeFields++;
                        Append("{{" + simpleName + "}}", IsBold(run));
                    }
                    continue;
                }

                var bold = IsBold(run);
                foreach (var child in run.ChildElements)
                {
                    switch (child)
                    {
                        case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.Begin:
                            fieldDepth++;
                            if (fieldDepth == 1)
                            {
                                instruction.Clear();
                                inResult = false;
                                suppressResult = false;
                            }
                            break;
                        case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.Separate:
                            if (fieldDepth == 1)
                            {
                                inResult = true;
                                if (MergeFieldName(instruction.ToString()) is { } fieldName)
                                {
                                    _mergeFields++;
                                    Append("{{" + fieldName + "}}", bold);
                                    suppressResult = true;
                                }
                            }
                            break;
                        case FieldChar fc when fc.FieldCharType?.Value == FieldCharValues.End:
                            if (fieldDepth == 1 && !inResult && MergeFieldName(instruction.ToString()) is { } noResult)
                            {
                                _mergeFields++;
                                Append("{{" + noResult + "}}", bold);
                            }
                            if (fieldDepth > 0) fieldDepth--;
                            if (fieldDepth == 0) { inResult = false; suppressResult = false; }
                            break;
                        case FieldCode code:
                            if (fieldDepth == 1 && !inResult) instruction.Append(code.Text);
                            break;
                        default:
                            // Field instructions aren't text; a merge field's
                            // «result» is replaced by its placeholder.
                            if (fieldDepth > 0 && (!inResult || suppressResult)) break;
                            AppendRunChild(child, bold, Append);
                            break;
                    }
                }
            }

            return MendPlaceholders(chars);
        }

        private static void AppendRunChild(OpenXmlElement child, bool bold, Action<string, bool> append)
        {
            switch (child)
            {
                case Text t:
                    append(t.Text, bold);
                    break;
                case TabChar or PositionalTab:
                    append(" ", bold);
                    break;
                case Break br when br.Type is null || br.Type.Value == BreakValues.TextWrapping:
                    append("\n", false);
                    break;
                case CarriageReturn:
                    append("\n", false);
                    break;
                case NoBreakHyphen:
                    append("-", bold);
                    break;
            }
        }

        private static string? MergeFieldName(string? instruction)
        {
            if (string.IsNullOrWhiteSpace(instruction)) return null;
            var m = MergeFieldInstruction.Match(instruction);
            if (!m.Success) return null;
            var name = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
            return name.Length == 0 || name.Contains('{') || name.Contains('}') ? null : name;
        }

        // A placeholder is bold only when its whole name was bold, so ** never
        // lands inside {{…}}.
        private static List<Ch> MendPlaceholders(List<Ch> chars)
        {
            var text = new string(chars.Select(c => c.C).ToArray());
            foreach (Match m in AnyPlaceholder.Matches(text))
            {
                var inner = m.Groups[1];
                var bold = inner.Length > 0
                           && Enumerable.Range(inner.Index, inner.Length)
                               .All(i => chars[i].Bold || char.IsWhiteSpace(chars[i].C));
                for (var i = m.Index; i < m.Index + m.Length; i++)
                    chars[i] = chars[i] with { Bold = bold };
            }
            return chars;
        }

        private static string Plain(List<Ch> chars) => new(chars.Select(c => c.C).ToArray());

        // Chars to markup, line by line (the parser reads ** per line, so bold
        // is closed at every line break). Whitespace stays outside the markers.
        private static string Markup(List<Ch> chars)
        {
            var lines = new List<string>();
            var line = new List<Ch>();
            foreach (var ch in chars)
            {
                if (ch.C == '\n')
                {
                    lines.Add(MarkupLine(line));
                    line = [];
                }
                else line.Add(ch);
            }
            lines.Add(MarkupLine(line));

            // Trailing blanks would end the paragraph early; inner blank lines
            // (two breaks in a row) become a paragraph break, which is fine.
            return string.Join("\n", lines.Select(l => l.TrimEnd())).Trim('\n');
        }

        private static string MarkupLine(List<Ch> line)
        {
            var sb = new StringBuilder();
            var pendingSpace = new StringBuilder();
            var open = false;
            foreach (var ch in line)
            {
                // Whitespace takes the weight of what's around it, so a bold
                // run opens and closes on a visible character — adjacent bold
                // runs merge, and an all-space "bold" run emits nothing.
                if (char.IsWhiteSpace(ch.C))
                {
                    pendingSpace.Append(ch.C);
                    continue;
                }
                if (ch.Bold != open)
                {
                    if (open) sb.Append("**").Append(pendingSpace);
                    else sb.Append(pendingSpace).Append("**");
                    open = ch.Bold;
                }
                else sb.Append(pendingSpace);
                pendingSpace.Clear();
                sb.Append(ch.C);
            }
            if (open) sb.Append("**");
            sb.Append(pendingSpace);
            return sb.ToString();
        }
    }

    // ─── Shared ──────────────────────────────────────────────────────────

    // The first heading, else the file name ("offer_letter-2026" → "offer letter 2026").
    private static string SuggestName(string body, string baseName)
    {
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("# ") || line.StartsWith("## "))
            {
                var heading = line.TrimStart('#').Replace("**", "").Trim();
                if (heading.Length > 0 && !heading.Contains("{{")) return Clip(heading);
            }
            break;
        }

        var fromFile = Regex.Replace(baseName.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();
        return Clip(fromFile.Length > 0 ? fromFile : "Imported letter");
    }

    private static string Clip(string s) => s.Length <= 160 ? s : s[..160].TrimEnd();

    private static void AddLengthWarning(string body, List<string> warnings)
    {
        if (body.Length > 20000)
            warnings.Add($"The text is {body.Length:N0} characters — a template holds up to 20,000. Shorten it before saving.");
    }
}
