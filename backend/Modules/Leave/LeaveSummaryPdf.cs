using AltomateHR.Api.Modules.Leave.Dtos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Leave;

// The yearly leave summary as production renders it: two A4 landscape pages
// per employee — a month-by-month matrix, then the approved-request detail.
// Sizes, colours and column widths follow production's leave-summary-pdf.tsx.
public static class LeaveSummaryPdf
{
    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    private const string Ink = "#1e1a2b";
    private const string Muted = "#6b6577";
    private const string HeaderBg = "#f1eff5";
    private const string AltRow = "#fafafa";
    private const string White  = "#ffffff";
    private const string Negative = "#be123c";   // a negative balance prints red

    public static byte[] Render(LeaveSummaryReportDto r) =>
        Document.Create(doc =>
        {
            MatrixPage(doc, r);
            DetailPage(doc, r);
        }).GeneratePdf();

    private static void MatrixPage(IDocumentContainer doc, LeaveSummaryReportDto r) =>
        doc.Page(page =>
        {
            Frame(page);
            page.Header().Element(h => Header(h, r, $"YEARLY LEAVE SUMMARY – {r.Year}", showReportDate: true));
            page.Content().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(18);    // #
                    c.RelativeColumn(3);     // Leave Type
                    c.ConstantColumn(30);    // Ent.
                    c.ConstantColumn(30);    // C/F
                    for (var i = 0; i < 12; i++) c.ConstantColumn(26);
                    c.ConstantColumn(34);    // Total
                    c.ConstantColumn(40);    // Balance
                });

                table.Header(h =>
                {
                    HeadCell(h, "#");
                    HeadCell(h, "Leave Type", left: true);
                    HeadCell(h, "Ent.", right: true);
                    HeadCell(h, "C/F", right: true);
                    foreach (var m in Months) HeadCell(h, m, center: true);
                    HeadCell(h, "Total", right: true);
                    HeadCell(h, "Balance", right: true);
                });

                var i = 0;
                foreach (var row in r.MonthlyRows)
                {
                    var bg = i % 2 == 1 ? AltRow : White;
                    Cell(table, bg).Text((i + 1).ToString()).FontSize(7.5f).FontColor(Muted);
                    Cell(table, bg).Text(row.LeaveTypeName).FontSize(7.5f).SemiBold();
                    Cell(table, bg).AlignRight().Text(Num(row.EntitledDays)).FontSize(7.5f);
                    Cell(table, bg).AlignRight().Text(Dash(row.CarriedDays)).FontSize(7.5f);
                    foreach (var v in row.Monthly)
                        Cell(table, bg).AlignCenter().Text(Dash(v)).FontSize(7.5f).FontColor(Muted);
                    Cell(table, bg).AlignRight().Text(Dash(row.Total)).FontSize(7.5f).SemiBold();
                    Cell(table, bg).AlignRight().Text(Num(row.Balance)).FontSize(7.5f)
                        .FontColor(row.Balance < 0 ? Negative : Ink);
                    i++;
                }
            });
            if (!r.MonthlyRows.Any())
                page.Content().PaddingTop(40).Text("No leave entitlements for this year.")
                    .FontSize(8).FontColor(Muted);
            page.Footer().Element(f => Footer(f, r));
        });

    private static void DetailPage(IDocumentContainer doc, LeaveSummaryReportDto r) =>
        doc.Page(page =>
        {
            Frame(page);
            page.Header().Element(h => Header(h, r,
                $"YEARLY LEAVE SUMMARY – {r.Year}  |  Leave Applications", showReportDate: false));
            page.Content().PaddingTop(8).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(62);   // From
                    c.ConstantColumn(62);   // To
                    c.RelativeColumn(2);    // Leave Type
                    c.ConstantColumn(34);   // Days
                    c.RelativeColumn(3);    // Reason
                    c.RelativeColumn(2);    // Attachment
                });
                table.Header(h =>
                {
                    HeadCell(h, "From", left: true);
                    HeadCell(h, "To", left: true);
                    HeadCell(h, "Leave Type", left: true);
                    HeadCell(h, "Days", right: true);
                    HeadCell(h, "Reason", left: true);
                    HeadCell(h, "Attachment", left: true);
                });

                var i = 0;
                foreach (var row in r.DetailRows)
                {
                    var bg = i % 2 == 1 ? AltRow : White;
                    Cell(table, bg).Text(Date(row.From)).FontSize(7.5f);
                    Cell(table, bg).Text(Date(row.To)).FontSize(7.5f);
                    Cell(table, bg).Text(row.LeaveTypeName).FontSize(7.5f);
                    Cell(table, bg).AlignRight().Text(Num(row.Days)).FontSize(7.5f);
                    Cell(table, bg).Text(row.Reason ?? "–").FontSize(7.5f).FontColor(Muted);
                    Cell(table, bg).Text(row.AttachmentName ?? "–").FontSize(7.5f).FontColor(Muted);
                    i++;
                }
            });
            if (!r.DetailRows.Any())
                page.Content().PaddingTop(40).Text($"No approved leave applications for {r.Year}.")
                    .FontSize(8).FontColor(Muted);
            page.Footer().Element(f => Footer(f, r));
        });

    private static void Frame(PageDescriptor page)
    {
        page.Size(PageSizes.A4.Landscape());
        page.MarginVertical(36);
        page.MarginHorizontal(28);
        page.DefaultTextStyle(t => t.FontFamily("Helvetica").FontSize(8).FontColor(Ink));
    }

    private static void Header(IContainer c, LeaveSummaryReportDto r, string title, bool showReportDate) =>
        c.Column(col =>
        {
            col.Item().Text(r.OrganizationName).FontSize(13).Bold();
            col.Item().Text(title).FontSize(10.5f).FontColor(Muted);
            col.Item().PaddingTop(5).Text(t =>
            {
                t.Span("Employee: ").FontSize(8.5f).Bold();
                t.Span(r.EmployeeLabel).FontSize(8.5f).FontColor(Muted);
            });
            if (showReportDate)
                col.Item().Text(t =>
                {
                    t.Span("Report Date: ").FontSize(8.5f).Bold();
                    t.Span(Date(r.ReportDate)).FontSize(8.5f).FontColor(Muted);
                });
            col.Item().PaddingTop(6).LineHorizontal(0.75f).LineColor(Muted);
        });

    private static void Footer(IContainer c, LeaveSummaryReportDto r) =>
        c.PaddingTop(8).Row(row =>
        {
            row.RelativeItem().Text($"Generated on {Date(r.ReportDate)}").FontSize(7).FontColor(Muted);
            row.RelativeItem().AlignRight().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7).FontColor(Muted));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        });

    private static void HeadCell(TableCellDescriptor h, string text,
        bool left = false, bool right = false, bool center = false)
    {
        var cell = h.Cell().Background(HeaderBg).PaddingVertical(5).PaddingHorizontal(3);
        var aligned = right ? cell.AlignRight() : center ? cell.AlignCenter() : cell;
        aligned.Text(text).FontSize(7).Bold().FontColor(Ink);
    }

    private static IContainer Cell(TableDescriptor t, string bg) =>
        t.Cell().Background(bg).PaddingVertical(4).PaddingHorizontal(3);

    // Zero renders as an en dash, matching production's numCell — a grid of
    // zeroes is far harder to scan than a grid of dashes.
    private static string Dash(double? v) => v is null or 0 ? "–" : Num(v.Value);

    private static string Num(double v) => v % 1 == 0 ? ((long)v).ToString() : v.ToString("0.##");

    private static string Date(DateTime d) => d.ToString("dd MMM yyyy");
}
