using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Payroll;

// The people CIMB's bulk-payroll file can't carry: its Beneficiary ID column
// takes an NRIC or a business registration number and has no passport
// option, so a foreign worker paid by passport is left out of that file
// (PayrollBankFileCimbTxt) and listed here instead — everything needed to
// pay them one by one in BizChannel@CIMB.
//
// The CIMB file + this sheet + the Manual payments sheet together cover every
// ringgit of net pay. Accounts are printed IN FULL: this is a list someone
// pays from, like the payment schedule.
public static class CimbPassportPayeesXlsx
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly string[] Headers =
    [
        "No.", "Emp no.", "Employee", "Passport / ID no.", "Nationality", "Bank", "Account number",
        "Account holder", "Net pay (RM)", "Reference", "Paid on",
    ];

    public static StatutoryFileResult Render(PayrollDocumentModel model, DateTime paymentDate)
    {
        var (rows, refusal) = PayrollBankRows.Select(model);
        if (refusal is not null) return refusal;

        var payees = rows.Where(r => PayrollBankFileCimbTxt.IsPassportPayee(r.Source)).ToList();
        if (payees.Count == 0)
        {
            return StatutoryFileResult.Refused(
                "Nobody on this run is paid by passport — everyone with a bank account is in the CIMB file.");
        }

        var reference = PayrollBankRows.Reference(model);

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Pay by hand in CIMB");

        sheet.Cell(1, 1).Value =
            $"{model.OrganizationName} — CIMB: pay by hand (passport holders), {model.PeriodLabel}";
        sheet.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13);
        sheet.Cell(2, 1).Value =
            $"{model.StatusLabel}. Not in the CIMB bulk-payroll file — CIMB's file only takes an NRIC. "
            + $"Pay each in BizChannel@CIMB by {paymentDate:dd/MM/yyyy}, then fill in Paid on.";
        sheet.Cell(2, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);

        const int headerRow = 4;
        for (var c = 0; c < Headers.Length; c++)
        {
            var cell = sheet.Cell(headerRow, c + 1);
            cell.Value = Headers[c];
            cell.Style.Font.SetBold();
            cell.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#F3F4F6"));
        }

        var r = headerRow;
        var n = 0;
        foreach (var p in payees.OrderBy(p => p.Source.EmployeeName, StringComparer.OrdinalIgnoreCase))
        {
            r++;
            n++;
            var s = p.Source;
            sheet.Cell(r, 1).Value = n;
            sheet.Cell(r, 2).Value = s.EmployeeCode;
            sheet.Cell(r, 3).Value = s.EmployeeName;
            // A passport in practice; any other non-NRIC kind (police, army)
            // is named so it isn't read as a passport.
            sheet.Cell(r, 4).Value = s.IdType is Employees.Entities.IdType.PASSPORT or null
                ? s.IdNumber?.Trim() ?? ""
                : $"{s.IdNumber?.Trim()} ({s.IdType})";
            sheet.Cell(r, 5).Value = s.Nationality ?? "";
            sheet.Cell(r, 6).Value = p.Bank.Name;
            // Text, so a leading zero survives.
            sheet.Cell(r, 7).SetValue(p.Account);
            sheet.Cell(r, 8).Value = PayrollBankRows.PayeeName(s);
            sheet.Cell(r, 9).Value = p.Amount;
            sheet.Cell(r, 9).Style.NumberFormat.Format = "#,##0.00";
            sheet.Cell(r, 10).Value = reference;
        }

        r++;
        sheet.Cell(r, 8).Value = $"Total ({n})";
        sheet.Cell(r, 8).Style.Font.SetBold();
        sheet.Cell(r, 9).FormulaA1 = $"SUM(I{headerRow + 1}:I{r - 1})";
        sheet.Cell(r, 9).Style.Font.SetBold();
        sheet.Cell(r, 9).Style.NumberFormat.Format = "#,##0.00";

        sheet.Columns().AdjustToContents();

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        var fileName = $"CIMB_Passport_Payees_{model.Run.PeriodMonth:D2}{model.Run.PeriodYear}.xlsx";
        return new StatutoryFileResult(true, fileName, buffer.ToArray(), ContentType, null);
    }
}
