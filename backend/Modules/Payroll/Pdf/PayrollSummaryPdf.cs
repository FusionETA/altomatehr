using AltomateHR.Api.Modules.Payroll.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// The whole run on one sheet: every employee's gross, statutory deductions and
// net, then the totals that get reconciled against the bank and the agencies.
//
// The column totals are computed from the ROWS shown rather than read off the
// run's cached figures. If those two ever disagreed, printing the cached one
// would hide the disagreement — and the payslips are the source of truth.
public static class PayrollSummaryPdf
{
    public const string ContentType = "application/pdf";

    public static byte[] Render(PayrollDocumentModel model) => Build(model).GeneratePdf();

    // The document itself, for rasterising in a visual check.
    public static IDocument Build(PayrollDocumentModel model) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.MarginVertical(30);
            page.MarginHorizontal(30);
            page.DefaultTextStyle(t => t
                .FontFamily(PdfFont.Family).FontSize(8).FontColor(PayrollPdfShared.Ink));

            page.Header().Element(c => Header(c, model));
            page.Content().Element(c => Body(c, model));
            page.Footer().Element(c => PayrollPdfShared.Footer(
                c, $"Payroll summary — {model.PeriodLabel}"));
        }));

    private static void Header(IContainer container, PayrollDocumentModel model) =>
        container.PaddingBottom(8).BorderBottom(1.5f).BorderColor(PayrollPdfShared.Accent)
            .PaddingBottom(4).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(model.OrganizationName).FontSize(12).Bold();
                    left.Item().Text("Payroll summary").FontSize(8.5f)
                        .FontColor(PayrollPdfShared.Muted);
                });
                row.ConstantItem(220).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text(model.PeriodLabel).FontSize(10).SemiBold();
                    right.Item().AlignRight()
                        .Text($"{model.Rows.Count} employee(s) · {model.StatusLabel}")
                        .FontSize(8).FontColor(PayrollPdfShared.Muted);
                });
            });

    private static void Body(IContainer container, PayrollDocumentModel model) =>
        container.PaddingTop(8).Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(58);    // employee no
                c.RelativeColumn(2.4f);  // name
                c.ConstantColumn(62);    // gross
                c.ConstantColumn(56);    // EPF ee
                c.ConstantColumn(52);    // SOCSO ee
                c.ConstantColumn(48);    // EIS ee
                c.ConstantColumn(48);    // SKBBK ee
                c.ConstantColumn(56);    // PCB
                c.ConstantColumn(56);    // other
                c.ConstantColumn(62);    // net
                c.ConstantColumn(62);    // employer cost
            });

            table.Header(h =>
            {
                Th(h, "Emp no.");
                Th(h, "Employee");
                Th(h, "Gross", right: true);
                Th(h, "EPF", right: true);
                Th(h, "SOCSO", right: true);
                Th(h, "EIS", right: true);
                Th(h, "SKBBK", right: true);
                Th(h, "PCB", right: true);
                Th(h, "Other", right: true);
                Th(h, "Net", right: true);
                Th(h, "Cost", right: true);
            });

            foreach (var row in model.Rows)
            {
                var p = row.Payslip;
                // Zakat and CP38 live inside TotalDeductions, so this column
                // is everything that is not one of the named statutory items.
                //
                // Every deduction MUST have a column here. This sheet exists
                // to be reconciled — Gross minus the deduction columns has to
                // equal Net, and a statutory item with nowhere to go (SKBBK
                // was exactly this) makes every row silently short.
                var other = p.TotalDeductions;

                Td(table, row.EmployeeCode);
                Td(table, row.EmployeeName);
                Money(table, p.GrossPay);
                Money(table, p.EpfEmployee);
                Money(table, p.SocsoEmployee);
                Money(table, p.EisEmployee);
                Money(table, p.SkbbkEmployee);
                Money(table, p.Pcb);
                Money(table, other);
                Money(table, p.NetPay, bold: true);
                Money(table, p.TotalCostToEmployer);
            }

            // Totals, summed from the rows above.
            Tf(table, string.Empty);
            Tf(table, "Total");
            MoneyFoot(table, model.Rows.Sum(r => r.Payslip.GrossPay));
            MoneyFoot(table, model.Rows.Sum(r => r.Payslip.EpfEmployee));
            MoneyFoot(table, model.Rows.Sum(r => r.Payslip.SocsoEmployee));
            MoneyFoot(table, model.Rows.Sum(r => r.Payslip.EisEmployee));
            MoneyFoot(table, model.Rows.Sum(r => r.Payslip.SkbbkEmployee));
            MoneyFoot(table, model.Rows.Sum(r => r.Payslip.Pcb));
            MoneyFoot(table, model.Rows.Sum(r => r.Payslip.TotalDeductions));
            MoneyFoot(table, model.Rows.Sum(r => r.Payslip.NetPay));
            MoneyFoot(table, model.Rows.Sum(r => r.Payslip.TotalCostToEmployer));
        });

    private static void Th(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().PaddingVertical(3)
            .BorderBottom(1).BorderColor(PayrollPdfShared.Rule);

        (right ? cell.AlignRight() : cell)
            .Text(text).FontSize(7).SemiBold().FontColor(PayrollPdfShared.Muted);
    }

    private static void Td(TableDescriptor table, string text) =>
        table.Cell().PaddingVertical(2).Text(text).FontSize(8);

    private static void Money(TableDescriptor table, decimal value, bool bold = false)
    {
        var span = table.Cell().PaddingVertical(2).AlignRight()
            .Text(PayrollPdfShared.Rm(value)).FontSize(8);

        if (bold) span.SemiBold();
    }

    private static void Tf(TableDescriptor table, string text) =>
        table.Cell().PaddingTop(4).BorderTop(1).BorderColor(PayrollPdfShared.Ink)
            .PaddingTop(3).Text(text).FontSize(8).Bold();

    private static void MoneyFoot(TableDescriptor table, decimal value) =>
        table.Cell().PaddingTop(4).BorderTop(1).BorderColor(PayrollPdfShared.Ink)
            .PaddingTop(3).AlignRight().Text(PayrollPdfShared.Rm(value)).FontSize(8).Bold();
}
