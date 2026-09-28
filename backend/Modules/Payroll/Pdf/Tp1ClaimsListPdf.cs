using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// "System must provide list of employees that claimed these TP Form. The list
// can be print and save." — LHDN MTD Spec 2026, Section E item 16.
//
// One payroll month: everyone with a TP1 deduction or rebate that month, and
// everyone who declared a previous employer this year on TP3 (Borang PCB/TP3).
public static class Tp1ClaimsListPdf
{
    public const string ContentType = "application/pdf";

    public sealed record Row
    {
        public required string EmployeeName { get; init; }
        public string? EmployeeCode { get; init; }
        public string? IdNumber { get; init; }
        public string? IncomeTaxNumber { get; init; }

        // TP1: the month's deductions (LP1) and the year's (ΣLP + LP1), and the
        // month's rebates (zakat, departure levy). Granted figures.
        public decimal Tp1Current { get; init; }
        public decimal Tp1Cumulative { get; init; }
        public decimal RebateCurrent { get; init; }
        public IReadOnlyList<string> ItemsClaimed { get; init; } = [];

        // TP3: the previous employer's remuneration declared for this year.
        public decimal? Tp3PriorRemuneration { get; init; }
    }

    public sealed record Model
    {
        public required string EmployerName { get; init; }
        public required int Year { get; init; }
        public required int Month { get; init; }
        public required IReadOnlyList<Row> Rows { get; init; }
        public required DateTime GeneratedAt { get; init; }
    }

    public static byte[] Render(Model model) => Build(model).GeneratePdf();

    public static IDocument Build(Model model) =>
        Document.Create(doc => doc.Page(page =>
        {
            PayrollPdfShared.Frame(page);
            page.Size(PageSizes.A4.Landscape());
            page.Header().Element(c => Header(c, model));
            page.Content().Element(c => Body(c, model));
            page.Footer().Element(c => PayrollPdfShared.Footer(c,
                $"Senarai tuntutan TP1 / TP3 · {model.EmployerName} · "
                + $"{Tp1FormPdf.MonthName(model.Month)} {model.Year} · dijana {PayrollPdfShared.Date(model.GeneratedAt)}"));
        }));

    private static void Header(IContainer container, Model m) =>
        container.PaddingBottom(8).BorderBottom(1.5f).BorderColor(PayrollPdfShared.Accent).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(m.EmployerName).FontSize(12).Bold();
                left.Item().Text("Senarai pekerja yang menuntut Borang PCB/TP1 dan PCB/TP3")
                    .FontSize(8.5f).FontColor(PayrollPdfShared.Muted);
            });
            row.ConstantItem(200).AlignRight().Text($"{Tp1FormPdf.MonthName(m.Month)} {m.Year}")
                .FontSize(10).SemiBold();
        });

    private static void Body(IContainer container, Model m) =>
        container.PaddingTop(8).Column(col =>
        {
            if (m.Rows.Count == 0)
            {
                col.Item().Text("Tiada pekerja menuntut TP1 atau TP3 bagi bulan ini.")
                    .FontSize(9).FontColor(PayrollPdfShared.Muted);
                return;
            }

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(22);
                    c.RelativeColumn(2.4f);
                    c.RelativeColumn(1f);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(1.3f);
                    c.RelativeColumn(1.6f);
                    c.ConstantColumn(64);
                    c.ConstantColumn(64);
                    c.ConstantColumn(58);
                    c.ConstantColumn(70);
                });

                table.Header(h =>
                {
                    foreach (var title in new[]
                             {
                                 "Bil.", "Nama", "No. Pekerja", "No. KP / Pasport", "No. Cukai (TIN)",
                                 "Item TP1", "TP1 Semasa", "TP1 Terkumpul", "Rebat Semasa", "TP3 (Saraan terdahulu)",
                             })
                    {
                        h.Cell().BorderBottom(1).BorderColor(PayrollPdfShared.Rule).PaddingVertical(3)
                            .Text(title).FontSize(7).SemiBold().FontColor(PayrollPdfShared.Muted);
                    }
                });

                var n = 0;
                foreach (var r in m.Rows)
                {
                    n++;
                    Cell(table, n.ToString());
                    Cell(table, r.EmployeeName);
                    Cell(table, r.EmployeeCode);
                    Cell(table, r.IdNumber);
                    Cell(table, r.IncomeTaxNumber);
                    Cell(table, r.ItemsClaimed.Count > 0 ? string.Join(", ", r.ItemsClaimed) : null);
                    Cell(table, Money(r.Tp1Current), right: true);
                    Cell(table, Money(r.Tp1Cumulative), right: true);
                    Cell(table, Money(r.RebateCurrent), right: true);
                    Cell(table, r.Tp3PriorRemuneration is { } p ? Money(p) : null, right: true);
                }

                // Totals, so the list reconciles against the run.
                table.Cell().ColumnSpan(6).BorderTop(1).BorderColor(PayrollPdfShared.Rule).PaddingVertical(3)
                    .Text($"Jumlah ({m.Rows.Count} pekerja)").FontSize(7.5f).SemiBold();
                TotalCell(table, m.Rows.Sum(r => r.Tp1Current));
                TotalCell(table, m.Rows.Sum(r => r.Tp1Cumulative));
                TotalCell(table, m.Rows.Sum(r => r.RebateCurrent));
                TotalCell(table, m.Rows.Sum(r => r.Tp3PriorRemuneration ?? 0m));
            });
        });

    private static string? Money(decimal value) => value > 0m ? PayrollPdfShared.Rm(value) : null;

    private static void Cell(TableDescriptor table, string? text, bool right = false)
    {
        var cell = table.Cell().BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(3);
        (right ? cell.AlignRight() : cell).Text(string.IsNullOrWhiteSpace(text) ? "—" : text).FontSize(7.5f);
    }

    private static void TotalCell(TableDescriptor table, decimal value) =>
        table.Cell().BorderTop(1).BorderColor(PayrollPdfShared.Rule).PaddingVertical(3).AlignRight()
            .Text(PayrollPdfShared.Rm(value)).FontSize(7.5f).SemiBold();
}
