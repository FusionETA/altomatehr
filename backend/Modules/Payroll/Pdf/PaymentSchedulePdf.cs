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
        // Only people who are actually owed money. A zero-net payslip is a
        // real payslip but not a payment, and listing it invites someone to
        // "fix" a missing bank account that does not matter.
        var payable = model.Rows.Where(r => r.Payslip.NetPay > 0m).ToList();

        container.PaddingTop(8).Column(col =>
        {
            col.Item().Table(table =>
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

                foreach (var row in payable)
                {
                    var bank = MalaysianBanks.Find(row.BankName);

                    Td(table, row.EmployeeCode);
                    Td(table, row.EmployeeName);
                    // The canonical name when the free text resolves, and the
                    // admin's own text when it does not — so an unrecognised
                    // bank is visible here rather than only failing later at
                    // bank-file time.
                    Td(table, bank?.Name ?? row.BankName ?? "—");
                    Td(table, row.BankAccountNumber ?? "—");
                    table.Cell().PaddingVertical(2).AlignRight()
                        .Text(PayrollPdfShared.Rm(row.Payslip.NetPay)).FontSize(8.5f);
                }

                table.Cell().ColumnSpan(4).PaddingTop(5)
                    .BorderTop(1).BorderColor(PayrollPdfShared.Ink).PaddingTop(3)
                    .Text($"Total — {payable.Count} payment(s)").FontSize(9).Bold();
                table.Cell().PaddingTop(5)
                    .BorderTop(1).BorderColor(PayrollPdfShared.Ink).PaddingTop(3)
                    .AlignRight().Text(PayrollPdfShared.Rm(payable.Sum(r => r.Payslip.NetPay)))
                    .FontSize(9).Bold();
            });

            // Anyone the bank file will not be able to pay, called out where
            // the approver is looking rather than discovered on upload.
            var unpayable = payable
                .Where(r => string.IsNullOrWhiteSpace(r.BankAccountNumber)
                            || MalaysianBanks.Find(r.BankName) is null)
                .ToList();

            if (unpayable.Count > 0)
            {
                col.Item().PaddingTop(14).Background("#fffbeb")
                    .Border(1).BorderColor("#fcd34d").Padding(8).Column(warn =>
                    {
                        warn.Item().Text($"{unpayable.Count} employee(s) cannot be paid by bank transfer")
                            .FontSize(8.5f).SemiBold().FontColor("#b45309");

                        foreach (var row in unpayable)
                        {
                            var reason = string.IsNullOrWhiteSpace(row.BankAccountNumber)
                                ? "no bank account on file"
                                : $"unrecognised bank \"{row.BankName}\"";

                            warn.Item().Text($"• {row.EmployeeName} — {reason}")
                                .FontSize(8).FontColor("#b45309");
                        }
                    });
            }
        });
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
