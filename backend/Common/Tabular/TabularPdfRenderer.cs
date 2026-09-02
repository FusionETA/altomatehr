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
    private const string HeaderBg = "#f1eff5";
    private const string AltRow = "#fafafa";
    private const string White = "#ffffff";
    private const string TotalsBg = "#f1eff5";

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
            page.Footer().Element(Footer);
        });

    private static void SheetPages(IDocumentContainer doc, TabularPdfHeader header, TabularSheet sheet) =>
        doc.Page(page =>
        {
            Frame(page);
            page.Header().Element(h => Heading(h, header, sheet));

            page.Content().PaddingTop(8).Element(content =>
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
                                .Text(head).FontSize(6.5f).Bold().FontColor(Ink);
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

            page.Footer().Element(Footer);
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
            col.Item().Text(header.OrganizationName).FontSize(13).Bold();
            col.Item().Text(sheet is null ? header.Title : $"{header.Title} – {sheet.Name}")
                .FontSize(10.5f).FontColor(Muted);

            // The filters live on the page, not just in the filename: a printed
            // report has to say what it covers, because the file it came from is
            // long gone by the time someone reads it.
            var caption = sheet?.Caption ?? header.Subtitle;
            if (!string.IsNullOrWhiteSpace(caption))
                col.Item().PaddingTop(4).Text(caption).FontSize(8.5f).FontColor(Muted);

            col.Item().PaddingTop(6).LineHorizontal(0.75f).LineColor(Muted);
        });

    private static void Footer(IContainer container) =>
        container.PaddingTop(8).Row(row =>
        {
            row.RelativeItem()
                .Text($"Generated on {DateTime.UtcNow:dd MMM yyyy HH:mm} UTC")
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
