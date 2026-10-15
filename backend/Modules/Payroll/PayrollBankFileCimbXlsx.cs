using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Payroll;

// BizChannel@CIMB bulk payroll, as CIMB's own Excel template — the layout
// the customer already uploads:
//
//   row 1   the template's field rules (red), kept as-is
//   row 2   *Beneficiary Name | Beneficiary ID | *BNM Code | *Account Number
//           | *Payment Amount | Reference Number
//   row 3+  one payee each
//
// Beneficiary ID is optional and left blank, as in the customer's working
// file. That is what lets an NRIC holder and a passport holder go in the same
// file the same way: the fixed-width .txt this replaced filled that column
// from the employee's NRIC and had no passport option, so foreign workers
// couldn't be paid through it at all.
//
// BNM Code is written as the template's dropdown label, "35-CIMB Bank
// Berhad" — the two-digit IBG code then the bank. Account Number is text (a
// leading zero survives); Payment Amount is a real number to two decimals.
public static class PayrollBankFileCimbXlsx
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // The template's own row 1 and row 2, word for word.
    private static readonly string[] Rules =
    [
        "Length : 40 (Char without '-' or '/')",
        "Length: 20 (Char without '-' or '/')",
        "Length: 2 (Num Only)",
        "Length: 16 (Num only)",
        "Length: 11(Num only)",
        "Length : 30 (Char without '-' or '/')",
    ];

    private static readonly string[] Headers =
    [
        "*Beneficiary Name", "Beneficiary ID", "*BNM Code", "*Account Number", "*Payment Amount",
        "Reference Number",
    ];

    private static readonly double[] Widths = [31.25, 27.625, 39.75, 19, 18.5, 28.125];

    // Where CIMB's dropdown names a bank differently from the register, its
    // label wins — the customer's file shows both of these.
    private static readonly Dictionary<string, string> CimbLabels = new()
    {
        ["35"] = "CIMB Bank Berhad",
        ["33"] = "Public Bank Berhad/ Public Finance Berhad",
    };

    private const int NameWidth = 40;
    private const int AccountWidth = 16;
    private const int ReferenceWidth = 30;

    public static StatutoryFileResult Render(PayrollDocumentModel model)
    {
        var (rows, refusal) = PayrollBankRows.Select(model);
        if (refusal is not null) return refusal;

        // The template's account column takes 16 digits. A longer one is never
        // cut to fit — that would send the money to a different account than
        // the one on file (and on the payment schedule). Named instead.
        var tooLong = rows
            .Where(r => r.Account.Length > AccountWidth)
            .Select(r => $"{r.Source.EmployeeName} ({r.Account.Length} digits)")
            .ToList();
        if (tooLong.Count > 0)
        {
            return StatutoryFileResult.Refused(
                $"CIMB's file takes an account number of at most {AccountWidth} digits. These are longer: "
                + string.Join("; ", tooLong)
                + ". Check each account number on the employee's profile.");
        }

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("EPF");   // the customer's template's sheet name

        for (var c = 0; c < Headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Rules[c];
            sheet.Cell(1, c + 1).Style.Font.SetFontColor(XLColor.Red);
            sheet.Cell(2, c + 1).Value = Headers[c];
            // Required columns ("*…") in red, as in the template.
            if (Headers[c].StartsWith('*')) sheet.Cell(2, c + 1).Style.Font.SetFontColor(XLColor.Red);
            sheet.Column(c + 1).Width = Widths[c];
        }

        var reference = NoSeparators(PayrollBankRows.Reference(model), ReferenceWidth);
        var r = 2;
        foreach (var row in rows)
        {
            r++;
            sheet.Cell(r, 1).Value = NoSeparators(PayrollBankRows.PayeeName(row.Source), NameWidth);
            // Beneficiary ID: optional, left blank (see the note above) —
            // formatted as text, as in the template.
            sheet.Cell(r, 2).Style.NumberFormat.Format = "@";
            sheet.Cell(r, 3).Value = $"{row.Bank.BnmCode}-{CimbLabels.GetValueOrDefault(row.Bank.BnmCode, row.Bank.Name)}";
            sheet.Cell(r, 4).SetValue(row.Account);   // checked ≤ 16 above
            sheet.Cell(r, 4).Style.NumberFormat.Format = "@";
            sheet.Cell(r, 5).Value = row.Amount;
            sheet.Cell(r, 5).Style.NumberFormat.Format = "#,##0.00;[Red]#,##0.00";
            sheet.Cell(r, 6).SetValue(reference);
            sheet.Cell(r, 6).Style.NumberFormat.Format = "@";
        }

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        var fileName = $"CIMB_Bulk_Payroll_{model.Run.PeriodMonth:D2}{model.Run.PeriodYear}.xlsx";
        return new StatutoryFileResult(true, fileName, buffer.ToArray(), ContentType, null);
    }

    // "Char without '-' or '/'": those become a space; anything outside
    // printable ASCII is dropped; runs of spaces collapse; then clamped.
    private static string NoSeparators(string? value, int width)
    {
        var kept = (value ?? string.Empty)
            .Replace('-', ' ').Replace('/', ' ')
            .Select(ch => ch is >= ' ' and <= '~' ? ch : ' ')
            .ToArray();
        var collapsed = string.Join(" ", new string(kept).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return PayrollBankRows.Clamp(collapsed, width);
    }
}
