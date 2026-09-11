using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;

namespace AltomateHR.Api.Modules.Payroll;

// The disbursement file: Public Bank's ECP (Enterprise Cash Payment) bulk
// upload, which is how the net pay actually leaves the company account.
//
// Three quirks of PB's validator, each learned the hard way and each worth
// keeping:
//
//   • The payment date in B1 must be a real Excel DATE, not text. A text
//     date is rejected with "Payment Date is invalid".
//   • Every amount is TEXT with exactly two decimals. A numeric cell drops a
//     trailing zero (3108.10 becomes 3108.1) and the row is rejected.
//   • The footer "TOTAL:" row is validated, and its total is text too.
//
// An employee whose bank name does not resolve is a REFUSAL, not a skipped
// row: quietly omitting them means someone does not get paid and nobody
// notices until they say so.
public static class PayrollBankFileXlsx
{
    public const string ContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const int ColumnCount = 21;

    private const string IdTypeHeader =
        "ID Type:\n\nFor Intrabank & IBG\nNI, OI, BR, PL, ML, PP\n\nFor Rentas\nNI, OI, BR, OT";

    private static readonly string[] Headers =
    [
        "Payment Type/ Mode : PBB/IBG/REN",
        "Bene Account No.",
        "BIC",
        "Bene Full Name",
        IdTypeHeader,
        "Bene Identification No / Passport",
        "Payment Amount (with 2 decimal points)",
        "Recipient Reference",
        "Other Payment Details",
        "Bene Email 1",
        "Bene Email 2",
        "Bene Mobile No. 1",
        "Bene Mobile No. 2",
        "Joint Bene Name",
        "Joint Beneficiary Identification No.",
        $"Joint {IdTypeHeader}",
        "E-mail Content Line 1",
        "E-mail Content Line 2",
        "E-mail Content Line 3",
        "E-mail Content Line 4",
        "E-mail Content Line 5",
    ];

    private static readonly string[] FormatHints =
    [
        "(M) - Char: 3 - A", "(M) - Char:20 - N", "(M) - Char: 11 - A",
        "(M) - Char: 120 - A", "(O) - Char: 2 - A", "(O) - Char: 29 - AN",
        "(M) - Char: 18 - N", "(M) - Char: 20 - AN", "(O) - Char: 20 - AN",
        "(O) - Char: 70 - AN", "(O) - Char: 70 - AN", "(O) - Char: 15 - N",
        "(O) - Char: 15 - N", "(O) - Char: 120 - A", "(O) - Char: 29 - AN",
        "(O) - Char: 2 - A", "(O) - Char: 40 - AN", "(O) - Char: 40 - AN",
        "(O) - Char: 40 - AN", "(O) - Char: 40 - AN", "(O) - Char: 40 - AN",
    ];

    // `payorAccountNo` is the 10-digit Public Bank account the salaries are
    // debited FROM. It is not a row in the sheet — PB's upload keys on it
    // through the FILENAME — so a file built without it is rejected by the
    // portal rather than failing here.
    public static StatutoryFileResult Render(
        PayrollDocumentModel model, DateTime paymentDate, string? payorAccountNo)
    {
        // Refused with the specific fix, like every other missing-field case
        // in this module: it is the admin's data problem, not an exception.
        var payor = new string((payorAccountNo ?? "").Where(char.IsDigit).ToArray());

        if (payor.Length == 0)
        {
            return StatutoryFileResult.Refused(
                "No Public Bank payor account number is set. Add it under Payroll "
                + "Settings → Bank before generating the disbursement file.");
        }

        if (payor.Length != 10)
        {
            return StatutoryFileResult.Refused(
                $"The Public Bank payor account number must be exactly 10 digits "
                + $"(it currently has {payor.Length}). Correct it under Payroll Settings → Bank.");
        }

        // Only rows that represent an actual bank payment.
        var candidates = model.Rows
            .Where(r => r.Payslip.NetPay > 0m
                     && !string.IsNullOrWhiteSpace(r.BankAccountNumber))
            .ToList();

        // Refuse rather than ship a file that silently omits someone.
        var unmatched = candidates
            .Where(r => MalaysianBanks.Find(r.BankName) is null)
            .Select(r => $"{r.EmployeeName} (\"{r.BankName}\")")
            .ToList();

        if (unmatched.Count > 0)
        {
            return StatutoryFileResult.Refused(
                "These employees' banks could not be matched to a recognised Malaysian bank: "
                + string.Join("; ", unmatched)
                + ". Correct the bank name on each affected employee's profile.");
        }

        if (candidates.Count == 0)
        {
            return StatutoryFileResult.Refused(
                "Nothing to disburse — every employee on this run has zero net pay or no bank "
                + "account on file.");
        }

        // Capped at 20 characters by PB. The 3-letter month keeps even
        // September inside the budget: "SALARY SEP 2026" is 15.
        var reference = Truncate(
            $"SALARY {MonthAbbreviation(model.Run.PeriodMonth)} {model.Run.PeriodYear}"
                .ToUpperInvariant(), 20);

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Payment");

        // Row 1 — the payment date, as a real date.
        sheet.Cell(1, 1).Value = "PAYMENT DATE :\n(DD/MM/YYYY)";
        sheet.Cell(1, 2).Value = paymentDate.Date;
        sheet.Cell(1, 2).Style.DateFormat.Format = "dd/mm/yyyy";

        for (var i = 0; i < ColumnCount; i++)
        {
            sheet.Cell(2, i + 1).Value = Headers[i];
            sheet.Cell(3, i + 1).Value = FormatHints[i];
        }

        var rowNumber = 4;
        decimal total = 0m;

        foreach (var row in candidates)
        {
            var bank = MalaysianBanks.Find(row.BankName)!;
            var (idType, idNumber) = MapIdentification(row.IdType, row.IdNumber);
            var amount = row.Payslip.NetPay;
            total += amount;

            sheet.Cell(rowNumber, 1).Value = bank.EcpMode.ToString();
            sheet.Cell(rowNumber, 2).Value = StatutoryFileFields.DigitsOnly(row.BankAccountNumber);
            sheet.Cell(rowNumber, 3).Value = bank.Bic;
            sheet.Cell(rowNumber, 4).Value = Truncate(
                string.IsNullOrWhiteSpace(row.BankAccountHolderName)
                    ? row.EmployeeName
                    : row.BankAccountHolderName!, 120);
            sheet.Cell(rowNumber, 5).Value = idType;
            sheet.Cell(rowNumber, 6).Value = idNumber;

            // Text, not a number — see the header note.
            sheet.Cell(rowNumber, 7).SetValue(Amount(amount));
            sheet.Cell(rowNumber, 8).Value = reference;
            sheet.Cell(rowNumber, 9).Value = Truncate($"EMP {row.EmployeeCode}".Trim(), 20);

            rowNumber++;
        }

        sheet.Cell(rowNumber, 1).Value = "TOTAL:";
        sheet.Cell(rowNumber, 7).SetValue(Amount(total));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        // PB's own convention: <10-digit account>PR<DDMMYY><NN>.xlsx. The
        // portal parses it, and will not accept the same filename twice in a
        // day — so the name is part of the format, not a nicety.
        var fileName =
            $"{payor}PR{paymentDate:ddMMyy}01.xlsx";
        return new StatutoryFileResult(true, fileName, stream.ToArray(), ContentType, null);
    }

    // Exactly two decimals, as text.
    private static string Amount(decimal value) =>
        value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    // PB's own identification codes. An employee with no ID on file leaves
    // both blank — the columns are optional, unlike the account number.
    private static (string Type, string Number) MapIdentification(IdType? idType, string? idNumber)
    {
        var raw = idNumber?.Trim() ?? string.Empty;
        if (raw.Length == 0) return (string.Empty, string.Empty);

        var alphanumeric = Truncate(StatutoryFileFields.AlphanumericOnly(raw), 29);
        var digits = Truncate(StatutoryFileFields.DigitsOnly(raw), 29);

        return idType switch
        {
            Employees.Entities.IdType.NRIC => ("NI", digits),
            Employees.Entities.IdType.PASSPORT => ("PP", alphanumeric),
            Employees.Entities.IdType.POLICE_NO => ("PL", alphanumeric),
            Employees.Entities.IdType.ARMY_NO => ("ML", alphanumeric),
            // Unknown type: the number is still useful, the code is not
            // worth guessing at.
            _ => (string.Empty, alphanumeric),
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string MonthAbbreviation(int month) =>
        month is < 1 or > 12
            ? month.ToString("D2")
            : System.Globalization.CultureInfo.InvariantCulture
                .DateTimeFormat.GetAbbreviatedMonthName(month);
}
