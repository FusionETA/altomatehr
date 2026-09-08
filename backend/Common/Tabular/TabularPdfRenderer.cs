using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Common.Tabular;

// Renders TabularSheets as an A4-landscape PDF report: the SAME sheet objects
// the CSV and XLSX writers consume, so a module describes its report once and
// gets three formats.
//
// Colours, sizes and the header/footer shape follow LeaveSummaryPdf (which
// itself follows production's renderer), so a claims report and a leave summary
// look like they came from the same system.
public static class TabularPdfRenderer
{
    private const string Ink = "#1e1a2b";
    private const string Muted = "#6b6577";
    // The table header is a solid dark band with white type, matching the report
    // production has been sending finance for years — it is the single strongest
    // cue that two exports came out of the same system.
    private const string HeaderBg = "#1f3352";
    private const string HeaderInk = "#ffffff";
    private const string AltRow = "#fafafa";
    private const string White = "#ffffff";
    private const string TotalsBg = "#f1eff5";
    private const string CardBorder = "#e3e0e8";

    // Landscape A4 is ~760pt of usable width. Past roughly this many columns the
    // cells get too narrow to read, which is why the modules hand the PDF a
    // curated subset rather than their full spreadsheet column set.
    public const int ComfortableColumnCount = 13;

    public static byte[] Render(IReadOnlyList<TabularSheet> sheets, TabularPdfHeader header) =>
        Document.Create(doc =>
        {
            // One page sequence per sheet: an XLSX tab has no PDF equivalent, and
            // a page break is the honest translation of "this is a separate table".
            if (sheets.Count == 0)
            {
                EmptyPage(doc, header);
                return;
            }

            foreach (var sheet in sheets) SheetPages(doc, header, sheet);
        }).GeneratePdf();

    private static void EmptyPage(IDocumentContainer doc, TabularPdfHeader header) =>
        doc.Page(page =>
        {
            Frame(page);
            page.Header().Element(h => Heading(h, header, null));
            page.Content().PaddingTop(32).Text("Nothing to report for this selection.")
                .FontSize(8).FontColor(Muted);
            page.Footer().Element(f => Footer(f, header));
        });

    private static void SheetPages(IDocumentContainer doc, TabularPdfHeader header, TabularSheet sheet) =>
        doc.Page(page =>
        {
            Frame(page);
            page.Header().Element(h => Heading(h, header, sheet));

            page.Content().PaddingTop(8).Column(body =>
            {
                if (sheet.Summary.Count > 0) body.Item().Element(c => SummaryBand(c, sheet));

                body.Item().Element(content =>
            {
                if (sheet.Rows.Count == 0 && sheet.TotalsRow is null)
                {
                    content.PaddingTop(24).Text("No rows matched this selection.")
                        .FontSize(8).FontColor(Muted);
                    return;
                }

                content.Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        // Relative widths from the header text length, clamped: a
                        // "Title" column needs the room a "Days" column doesn't,
                        // but one long header must not squeeze everything else out.
                        foreach (var head in sheet.Headers)
                            columns.RelativeColumn(Math.Clamp(head.Length / 6f, 1f, 2.5f));
                    });

                    // Repeated on every page — a multi-page table whose header
                    // only appears once is unreadable from page two onward.
                    table.Header(h =>
                    {
                        foreach (var head in sheet.Headers)
                            h.Cell().Background(HeaderBg).PaddingVertical(5).PaddingHorizontal(3)
                                .Text(head.ToUpperInvariant())
                                .FontSize(6.5f).Bold().FontColor(HeaderInk).LetterSpacing(0.04f);
                    });

                    var index = 0;
                    foreach (var row in sheet.Rows)
                    {
                        var background = index % 2 == 1 ? AltRow : White;
                        for (var c = 0; c < sheet.Headers.Count; c++)
                        {
                            var value = c < row.Count ? row[c] : string.Empty;
                            table.Cell().Background(background).PaddingVertical(3.5f).PaddingHorizontal(3)
                                .Text(value).FontSize(6.5f);
                        }
                        index++;
                    }

                    if (sheet.TotalsRow is { } totals)
                    {
                        for (var c = 0; c < sheet.Headers.Count; c++)
                        {
                            var value = c < totals.Count ? totals[c] : string.Empty;
                            table.Cell()
                                .Background(TotalsBg)
                                .BorderTop(0.75f).BorderColor(Muted)
                                .PaddingVertical(4).PaddingHorizontal(3)
                                .Text(value).FontSize(6.5f).Bold();
                        }
                    }
                });
            });
            });

            page.Footer().Element(f => Footer(f, header));
        });

    private static void Frame(PageDescriptor page)
    {
        page.Size(PageSizes.A4.Landscape());
        page.MarginVertical(36);
        page.MarginHorizontal(28);
        page.DefaultTextStyle(t => t.FontFamily("Helvetica").FontSize(8).FontColor(Ink));
    }

    private static void Heading(IContainer container, TabularPdfHeader header, TabularSheet? sheet) =>
        container.Column(col =>
        {
            col.Item().Text(header.OrganizationName.ToUpperInvariant())
                .FontSize(13).Bold().FontColor(HeaderBg);

            // The sheet name is only worth appending when it says something the
            // title does not. "Claims Report – Claims" was the old output: the
            // suffix is for multi-sheet reports, not for one that repeats itself.
            var subtitle = sheet is null || Echoes(header.Title, sheet.Name)
                ? header.Title
                : $"{header.Title} – {sheet.Name}";
            col.Item().Text(subtitle).FontSize(10.5f).FontColor(Muted);

            // The filters live on the page, not just in the filename: a printed
            // report has to say what it covers, because the file it came from is
            // long gone by the time someone reads it.
            var caption = sheet?.Caption ?? header.Subtitle;
            if (!string.IsNullOrWhiteSpace(caption))
                col.Item().PaddingTop(4).Text(caption).FontSize(8.5f).FontColor(Muted);

            col.Item().PaddingTop(6).LineHorizontal(0.75f).LineColor(Muted);
        });

    // The headline figures, as bordered cards above the table. A printed report
    // gets read away from the screen that produced it, so the two numbers an
    // approver actually wants have to be on the page rather than inferred by
    // scanning rows.
    private static void SummaryBand(IContainer container, TabularSheet sheet) =>
        container.PaddingBottom(10).Row(row =>
        {
            foreach (var (label, value) in sheet.Summary)
            {
                row.AutoItem().PaddingRight(8).Border(0.75f).BorderColor(CardBorder)
                    .PaddingVertical(6).PaddingHorizontal(10)
                    .Column(card =>
                    {
                        card.Item().Text(label.ToUpperInvariant())
                            .FontSize(6).FontColor(Muted).LetterSpacing(0.08f);
                        card.Item().PaddingTop(2).Text(value).FontSize(11).Bold().FontColor(Ink);
                    });
            }

            row.RelativeItem();   // soaks up the rest so the cards stay left-packed
        });

    // "Claims Report" vs "Claims" — one already contains the other, so joining
    // them adds nothing.
    private static bool Echoes(string title, string sheetName) =>
        title.Contains(sheetName, StringComparison.OrdinalIgnoreCase) ||
        sheetName.Contains(title, StringComparison.OrdinalIgnoreCase);

    private static void Footer(IContainer container, TabularPdfHeader header) =>
        container.PaddingTop(8).Row(row =>
        {
            // The org name repeats down here because a page torn out of a
            // stapled report has to still say whose report it is.
            row.RelativeItem()
                .Text($"{header.OrganizationName} · Generated {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC")
                .FontSize(7).FontColor(Muted);

            row.RelativeItem().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7).FontColor(Muted));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });
}

// The masthead of a rendered report: who it belongs to, what it is, and (unless
// a sheet overrides it with its own Caption) what it covers.
public sealed record TabularPdfHeader(string OrganizationName, string Title, string? Subtitle = null);
