using System.Text;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Modules.Payroll;

// Maybank2E-RC Universal Payment File (spec v4.3) — the pipe-delimited TXT
// Maybank2E → Bulk Payment accepts.
//
// Three record types, one per line:
//   00|<header>   29 fields  — Corporate ID + Client Batch ID
//   01|<record>   337 fields — one per employee
//   99|<trailer>  29 fields  — count + total debiting amount
//
// Every field position is emitted even when blank. The spec numbers fillers
// 148-336 as real fields and Maybank's parser is POSITIONAL: trailing empties
// are harmless, a missing one shifts every later value into the wrong column.
//
// Payroll uses Provider Product Group "Staff Payroll" (the MY entry in
// Appendix Table 2), with the mode chosen per employee:
//   IT — Book Transfer Third Party, when the employee also banks with Maybank
//   IG — Outward ACH (IBG) for every other bank, which needs the bene BIC
// The same intra/inter split as PB ECP's PBB/IBG.
public static class PayrollBankFileMbbTxt
{
    public const string ContentType = "text/plain";

    private const int HeaderFields = 29;
    private const int RecordFields = 337;
    private const int FooterFields = 29;

    // Appendix Table 2, row 30 — modes IA, IT, IG, IM.
    private const string ProductGroup = "Staff Payroll";
    private const string Currency = "MYR";

    // Characters Maybank accepts (General Information, page 3). The pipe is
    // listed as supported but is also the delimiter, so it is stripped here —
    // leaving one in a value would silently split the record.
    private static bool Supported(char c) =>
        c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z'
        || " !\"#$%&'()*+,-./{}~:;<=>?@[]^_`\\".Contains(c);

    public static StatutoryFileResult Render(
        PayrollDocumentModel model,
        DateTime valueDate,
        string? payorAccountNo,
        string? organisationCode)
    {
        // Corporate ID is issued by Maybank when M2E is set up; it reuses the
        // bank-agnostic organisation-code setting, as CIMB's does.
        var corporateId = Field(organisationCode, 30);
        if (corporateId.Length == 0)
        {
            return StatutoryFileResult.Refused(
                "No Maybank Corporate ID is set. Add it under Payroll Settings → Bank → "
                + "Organisation code before generating the Maybank file.");
        }

        var debitAccount = Digits(payorAccountNo, 20);
        if (debitAccount.Length == 0)
        {
            return StatutoryFileResult.Refused(
                "No Maybank debiting account number is set. Add it under Payroll Settings → "
                + "Bank before generating the Maybank file.");
        }

        var (rows, refusal) = PayrollBankRows.Select(model);
        if (refusal is not null) return refusal;

        var maybank = MalaysianBanks.Find("Malayan Banking Berhad")!;
        var reference = PayrollBankRows.Reference(model);
        var valueDateText = valueDate.ToString("ddMMyyyy");
        var periodTag = $"{model.Run.PeriodYear}{model.Run.PeriodMonth:D2}";

        var lines = new List<string>
        {
            // Client Batch ID must be unique per submission for the client's
            // own reconciliation — org code + period keeps it stable and
            // readable.
            Line(HeaderFields, new()
            {
                [1] = "00",
                [2] = corporateId,
                [3] = Field($"PR{periodTag}{corporateId}", 30),
            }),
        };

        decimal total = 0m;

        foreach (var row in rows)
        {
            // Intra-Maybank credits are a book transfer and must leave the
            // bene bank code BLANK; everything else goes over ACH/IBG and
            // carries the beneficiary bank's BIC.
            var intra = row.Bank.HlbCode == maybank.HlbCode;
            total += row.Amount;

            var fields = new Dictionary<int, string>
            {
                [1] = "01",
                [2] = intra ? "IT" : "IG",
                [3] = ProductGroup,
                [5] = valueDateText,
                // Unique within the file. Sequence guarantees that even when
                // two employees share an employee code.
                [8] = Field($"{periodTag}{row.Sequence:D4}", 30),
                [9] = Field(reference, 55),
                [10] = Field($"Salary {periodTag}", 55),
                [11] = Currency,
                [12] = row.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                // Debit and transaction currency are both MYR, so the amount
                // is already in the debit account's currency.
                [13] = "Y",
                [14] = Currency,
                [15] = debitAccount,
                [16] = Digits(row.Account, 35),
                [19] = row.Source.IsLocalOrPr ? "Y" : "N",
                [20] = Field(PayrollBankRows.PayeeName(row.Source), 40),
                [37] = intra ? "" : Field(row.Bank.Bic, 11),
            };

            foreach (var (position, value) in IdFields(row.Source.IdType, row.Source.IdNumber))
            {
                fields[position] = value;
            }

            lines.Add(Line(RecordFields, fields));
        }

        lines.Add(Line(FooterFields, new()
        {
            [1] = "99",
            [2] = rows.Count.ToString(),
            [3] = total.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
            // Field 4 (Hashing Value) is conditional — the formula is issued
            // by Maybank per corporate. Left blank until a customer needs it.
        }));

        // CRLF with a trailing newline, matching the sample files Maybank's
        // own converter emits.
        var content = Encoding.UTF8.GetBytes(string.Join("\r\n", lines) + "\r\n");
        var fileName = $"MBB_M2E_Payroll_{model.Run.PeriodMonth:D2}{model.Run.PeriodYear}.txt";
        return new StatutoryFileResult(true, fileName, content, ContentType, null);
    }

    // Maybank splits identification across FOUR columns (fields 25-28) rather
    // than PB ECP's single "type + number" pair: the number goes in the column
    // matching its kind, and the others stay blank.
    private static Dictionary<int, string> IdFields(IdType? idType, string? idNumber)
    {
        var raw = idNumber?.Trim() ?? string.Empty;
        if (raw.Length == 0) return [];

        // 25 = new IC, 26 = old IC, 27 = business registration, 28 = passport.
        return idType switch
        {
            IdType.NRIC => new() { [25] = Digits(raw, 20) },
            // Maybank has no dedicated police/army column on the payment
            // record — the spec groups them with passport in field 28.
            IdType.PASSPORT or IdType.POLICE_NO or IdType.ARMY_NO => new() { [28] = Field(raw, 20) },
            // Unknown type: an all-digits value is almost certainly an IC,
            // anything else goes to the column that accepts letters.
            _ => raw.All(c => !char.IsLetter(c))
                ? new() { [25] = Digits(raw, 20) }
                : new() { [28] = Field(raw, 20) },
        };
    }

    // One delimited line from a sparse map of 1-based field number → value.
    // Positions not supplied are emitted as empty fields.
    private static string Line(int total, Dictionary<int, string> values)
    {
        var cells = new string[total];
        Array.Fill(cells, string.Empty);
        foreach (var (position, value) in values) cells[position - 1] = value;
        return string.Join("|", cells);
    }

    private static string Field(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var kept = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            // The delimiter becomes a space rather than vanishing, so two
            // words don't silently run together.
            if (c == '|' || char.IsWhiteSpace(c)) kept.Append(' ');
            else if (Supported(c)) kept.Append(c);
        }

        var collapsed = string.Join(" ", kept.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return PayrollBankRows.Clamp(collapsed, maxLength);
    }

    private static string Digits(string? value, int maxLength) =>
        PayrollBankRows.Clamp(StatutoryFileFields.DigitsOnly(value ?? string.Empty), maxLength);
}
