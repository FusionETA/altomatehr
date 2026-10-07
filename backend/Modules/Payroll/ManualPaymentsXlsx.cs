using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Payroll;

// The Manual payments sheet: everyone on a run the bank payroll file does NOT
// pay — Other bank / e-wallet (Merchantrade, overseas banks), Cash, Cheque,
// and any bank-transfer employee missing an account number — for finance to
// pay by hand. The rule for who is on it is PayrollPayments, the same one the
// bank file uses, so the two sheets together cover every ringgit of net pay.
//
// Accounts are printed IN FULL, as on the payment schedule: this is the list
// someone pays from. Amounts are real numbers, so the sheet totals itself.
public static class ManualPaymentsXlsx
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] Headers =
    [
        "Emp no.", "Employee", "Payment method", "Bank / provider", "Account number",
        "Account holder", "Net pay (RM)", "Note", "Paid on", "Reference",
    ];

    public static byte[] Render(PayrollDocumentModel model)
    {
        var rows = PayrollPayments.Manual(model);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Manual payments");

        sheet.Cell(1, 1).Value = $"{model.OrganizationName} — manual payments, {model.PeriodLabel}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        sheet.Cell(2, 1).Value =
            $"{model.StatusLabel}. Not in the bank payroll file — pay each by hand, then fill in Paid on and Reference.";
        sheet.Cell(2, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);

        const int headerRow = 4;
        for (var c = 0; c < Headers.Length; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#EEE8F5"));
        }

        var r = headerRow + 1;
        foreach (var m in rows)
        {
            sheet.Cell(r, 1).Value = m.Row.EmployeeCode;
            sheet.Cell(r, 2).Value = m.Row.EmployeeName;
            sheet.Cell(r, 3).Value = m.Method;
            sheet.Cell(r, 4).Value = m.Row.BankName ?? "";
            // Text, so a long account number keeps its digits and leading zeros.
            sheet.Cell(r, 5).SetValue(m.Row.BankAccountNumber ?? "");
            sheet.Cell(r, 6).Value = PayrollBankRows.PayeeName(m.Row);
            sheet.Cell(r, 7).Value = m.Row.Payslip.NetPay;
            sheet.Cell(r, 7).Style.NumberFormat.Format = "#,##0.00";
            sheet.Cell(r, 8).Value = m.Issue ?? "";
            if (m.Issue is not null) sheet.Cell(r, 8).Style.Font.SetFontColor(XLColor.FromHtml("#B45309"));
            r++;
        }

        if (rows.Count == 0)
        {
            sheet.Cell(r, 1).Value = "Nobody — everyone owed pay on this run is in the bank file.";
            r++;
        }

        // Totals: per method, then overall.
        r++;
        foreach (var group in rows.GroupBy(m => m.Method))
        {
            sheet.Cell(r, 6).Value = $"{group.Key} ({group.Count()})";
            sheet.Cell(r, 7).Value = group.Sum(m => m.Row.Payslip.NetPay);
            sheet.Cell(r, 7).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }
        sheet.Cell(r, 6).Value = $"Total ({rows.Count})";
        sheet.Cell(r, 7).Value = rows.Sum(m => m.Row.Payslip.NetPay);
        sheet.Cell(r, 6).Style.Font.SetBold();
        sheet.Cell(r, 7).Style.Font.SetBold().NumberFormat.Format = "#,##0.00";

        sheet.Columns(1, Headers.Length).AdjustToContents(headerRow, r);
        sheet.Column(1).Width = Math.Max(sheet.Column(1).Width, 10);
        sheet.SheetView.FreezeRows(headerRow);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
