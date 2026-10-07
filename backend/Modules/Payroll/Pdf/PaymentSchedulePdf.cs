using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AltomateHR.Api.Modules.Payroll.Pdf;

// The disbursement list: who is paid, how much, and into which account.
//
// This is the sheet a finance approver signs against the bank file, so it
// shows the same rows the bank file will contain and totals them the same
// way. Bank accounts are printed IN FULL here — unlike a payslip, this
// document exists precisely so someone can verify the destination before the
// money moves.
public static class PaymentSchedulePdf
{
    public const string ContentType = "application/pdf";

    public static byte[] Render(PayrollDocumentModel model) => Build(model).GeneratePdf();

    // The document itself, for rasterising in a visual check.
    public static IDocument Build(PayrollDocumentModel model) =>
        Document.Create(doc => doc.Page(page =>
        {
            PayrollPdfShared.Frame(page);
            page.Header().Element(c => Header(c, model));
            page.Content().Element(c => Body(c, model));
            page.Footer().Element(c => PayrollPdfShared.Footer(
                c, $"Payment schedule — {model.PeriodLabel}. Verify against the bank file before release."));
        }));

    private static void Header(IContainer container, PayrollDocumentModel model) =>
        container.PaddingBottom(8).BorderBottom(1.5f).BorderColor(PayrollPdfShared.Accent)
            .PaddingBottom(4).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(model.OrganizationName).FontSize(12).Bold();
                    left.Item().Text("Payment schedule").FontSize(8.5f)
                        .FontColor(PayrollPdfShared.Muted);
                });
                row.ConstantItem(200).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text(model.PeriodLabel).FontSize(10).SemiBold();
                    right.Item().AlignRight().Text(model.StatusLabel)
                        .FontSize(8).FontColor(PayrollPdfShared.Muted);
                });
            });

    private static void Body(IContainer container, PayrollDocumentModel model)
    {
        // Two lists, by the same rule the bank file and the Manual payments
        // sheet use (PayrollPayments), so what the approver signs matches what
        // actually moves: the bank file's rows, then everyone paid by hand.
        // Zero-net payslips are not payments and appear in neither.
        var bankFile = model.Rows.Where(PayrollPayments.InBankFile).ToList();
        var manual = PayrollPayments.Manual(model);

        container.PaddingTop(8).Column(col =>
        {
            col.Item().Text($"Bank payroll file — {bankFile.Count} payment(s)").FontSize(9.5f).Bold();
            col.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(50);    // no
                    c.RelativeColumn(2f);    // name
                    c.RelativeColumn(1.6f);  // bank
                    c.RelativeColumn(1.4f);  // account
                    c.ConstantColumn(70);    // net
                });

                table.Header(h =>
                {
                    Th(h, "Emp no.");
                    Th(h, "Employee");
                    Th(h, "Bank");
                    Th(h, "Account");
                    Th(h, "Net pay", right: true);
                });

                foreach (var row in bankFile)
                {
                    var bank = MalaysianBanks.Find(row.BankName);
                    Td(table, row.EmployeeCode);
                    Td(table, row.EmployeeName);
                    // The canonical name when the free text resolves, and the
                    // admin's own text when it does not — so an unrecognised
                    // bank is visible here rather than only failing at bank-file time.
                    Td(table, bank?.Name ?? row.BankName ?? "—");
                    Td(table, row.BankAccountNumber ?? "—");
                    table.Cell().PaddingVertical(2).AlignRight()
                        .Text(PayrollPdfShared.Rm(row.Payslip.NetPay)).FontSize(8.5f);
                }

                TotalRow(table, 4, "Bank file total", bankFile.Sum(r => r.Payslip.NetPay));
            });

            col.Item().PaddingTop(14).Text($"Paid manually — {manual.Count} payment(s)").FontSize(9.5f).Bold();
            col.Item().Text("Not in the bank file: other banks and e-wallets, cash, cheque, and anyone missing bank details.")
                .FontSize(7.5f).FontColor(PayrollPdfShared.Muted);

            if (manual.Count == 0)
            {
                col.Item().PaddingTop(4).Text("None — everyone owed pay is in the bank file.").FontSize(8.5f);
            }
            else
            {
                col.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(50);    // no
                        c.RelativeColumn(1.8f);  // name
                        c.RelativeColumn(1.3f);  // method
                        c.RelativeColumn(1.5f);  // bank / provider
                        c.RelativeColumn(1.3f);  // account
                        c.ConstantColumn(70);    // net
                    });

                    table.Header(h =>
                    {
                        Th(h, "Emp no.");
                        Th(h, "Employee");
                        Th(h, "Method");
                        Th(h, "Bank / provider");
                        Th(h, "Account");
                        Th(h, "Net pay", right: true);
                    });

                    foreach (var m in manual)
                    {
                        Td(table, m.Row.EmployeeCode);
                        table.Cell().PaddingVertical(2).Column(c =>
                        {
                            c.Item().Text(m.Row.EmployeeName).FontSize(8.5f);
                            if (m.Issue is not null)
                                c.Item().Text(m.Issue).FontSize(7).FontColor("#b45309");
                        });
                        Td(table, m.Method);
                        Td(table, m.Row.BankName ?? "—");
                        Td(table, m.Row.BankAccountNumber ?? "—");
                        table.Cell().PaddingVertical(2).AlignRight()
                            .Text(PayrollPdfShared.Rm(m.Row.Payslip.NetPay)).FontSize(8.5f);
                    }

                    TotalRow(table, 5, "Manual total", manual.Sum(m => m.Row.Payslip.NetPay));
                });
            }

            var all = bankFile.Sum(r => r.Payslip.NetPay) + manual.Sum(m => m.Row.Payslip.NetPay);
            col.Item().PaddingTop(12).BorderTop(1.5f).BorderColor(PayrollPdfShared.Ink).PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text($"Total net pay — {bankFile.Count + manual.Count} payment(s)").FontSize(9.5f).Bold();
                row.ConstantItem(90).AlignRight().Text(PayrollPdfShared.Rm(all)).FontSize(9.5f).Bold();
            });
        });
    }

    private static void TotalRow(TableDescriptor table, int labelSpan, string label, decimal amount)
    {
        table.Cell().ColumnSpan((uint)labelSpan).PaddingTop(5)
            .BorderTop(1).BorderColor(PayrollPdfShared.Ink).PaddingTop(3)
            .Text(label).FontSize(9).Bold();
        table.Cell().PaddingTop(5)
            .BorderTop(1).BorderColor(PayrollPdfShared.Ink).PaddingTop(3)
            .AlignRight().Text(PayrollPdfShared.Rm(amount)).FontSize(9).Bold();
    }

    private static void Th(TableCellDescriptor header, string text, bool right = false)
    {
        var cell = header.Cell().PaddingVertical(3)
            .BorderBottom(1).BorderColor(PayrollPdfShared.Rule);

        (right ? cell.AlignRight() : cell)
            .Text(text).FontSize(7.5f).SemiBold().FontColor(PayrollPdfShared.Muted);
    }

    private static void Td(TableDescriptor table, string text) =>
        table.Cell().PaddingVertical(2).Text(text).FontSize(8.5f);
}
