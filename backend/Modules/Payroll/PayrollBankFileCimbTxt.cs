using System.Text;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Modules.Payroll;

// BizChannel@CIMB bulk-payroll file — the fixed-width TXT CIMB's BizConverter
// emits for "Bulk Payments (Without Email)", uploaded at BizChannel@CIMB →
// Bulk Payments → Payroll (File Format: TXT, File Type: Non Encrypted).
//
// Three record types, CRLF-terminated:
//   01 header  — org code + company name + payment date
//   02 detail  — one per employee
//   03 trailer — record count + total amount
//
// ── Field layout ────────────────────────────────────────────────────────
// Header (73 chars)
//   1-2     2  Record type "01"
//   3-7     5  Autopay Organisation Code (issued by CIMB)
//   8-47   40  Company name
//   48-55   8  Payment date DDMMYYYY
//   56-71  16  Zero-filled (see UNVERIFIED)
//   72-73   2  Blank
//
// Detail (127 chars)
//   1-2     2  Record type "02"
//   3-4     2  BNM bank code
//   5-20   16  Beneficiary account number
//   21-25   5  Blank (see UNVERIFIED)
//   26-65  40  Beneficiary name
//   66-76  11  Payment amount in SEN (no decimal point)
//   77-106 30  Reference number
//   107-126 20 Beneficiary ID (NRIC / passport)
//   127     1  ID type (see UNVERIFIED)
//
// Trailer (21 chars)
//   1-2     2  Record type "03"
//   3-8     6  Record count
//   9-21   13  Total amount in SEN
//
// ── UNVERIFIED ──────────────────────────────────────────────────────────
// The layout was reverse-engineered from a BizConverter-produced sample plus
// the BizConverter template's column widths, because CIMB's published guide
// covers the upload UI and not the file spec. Three positions could not be
// pinned down from one sample and are emitted exactly as the sample had them:
//
//   • Header 56-71 — sixteen '0's in the sample.
//   • Detail 21-25 — blank in the sample.
//   • Detail 127   — '2' for all three sample rows, every one of which had a
//     New NRIC. Modelled as an ID-type code on the usual Malaysian convention
//     (1 = old IC, 2 = new IC, 3 = passport, 4 = other). Only '2' is
//     corroborated.
//
// Verify against a real BizChannel upload before trusting this in production,
// and re-check IdTypeCode if a non-NRIC employee is ever in a run.
public static class PayrollBankFileCimbTxt
{
    public const string ContentType = "text/plain";

    private const int HeaderOrgCode = 5;
    private const int HeaderCompanyName = 40;
    private const int DetailAccount = 16;
    private const int DetailGap = 5;
    private const int DetailName = 40;
    private const int DetailAmount = 11;
    private const int DetailReference = 30;
    private const int DetailBeneId = 20;
    private const int TrailerCount = 6;
    private const int TrailerTotal = 13;

    public static StatutoryFileResult Render(
        PayrollDocumentModel model, DateTime paymentDate, string? organisationCode)
    {
        // "Autopay Organisation Code" in CIMB's template, issued by their
        // Business Call Centre when bulk payroll is enabled. Stored in the
        // bank-agnostic organisation-code setting.
        var orgCode = StatutoryFileFields.DigitsOnly(organisationCode ?? string.Empty);

        if (orgCode.Length == 0)
        {
            return StatutoryFileResult.Refused(
                "No CIMB Organisation Code is set. Add it under Payroll Settings → Bank → "
                + "Organisation code before generating the CIMB file.");
        }

        if (orgCode.Length > HeaderOrgCode)
        {
            return StatutoryFileResult.Refused(
                $"The CIMB Organisation Code must be at most {HeaderOrgCode} digits (it currently "
                + $"has {orgCode.Length}). Correct it under Payroll Settings → Bank.");
        }

        var (rows, refusal) = PayrollBankRows.Select(model);
        if (refusal is not null) return refusal;

        var reference = PayrollBankRows.Reference(model);

        var lines = new List<string>
        {
            string.Concat(
                "01",
                orgCode.PadRight(HeaderOrgCode),
                Text(model.OrganizationName, HeaderCompanyName),
                paymentDate.ToString("ddMMyyyy"),
                new string('0', 16),
                "  "),
        };

        long totalSen = 0;

        foreach (var row in rows)
        {
            var sen = PayrollBankRows.ToSen(row.Amount);
            totalSen += sen;

            var idNumber = row.Source.IdNumber?.Trim() ?? string.Empty;

            lines.Add(string.Concat(
                "02",
                row.Bank.BnmCode,
                DigitsPadded(row.Account, DetailAccount),
                new string(' ', DetailGap),
                TextNoSeparators(PayrollBankRows.PayeeName(row.Source), DetailName),
                ZeroPad(sen, DetailAmount),
                TextNoSeparators(reference, DetailReference),
                // An NRIC goes in digits-only; a passport keeps its letters.
                row.Source.IdType == IdType.NRIC
                    ? DigitsPadded(idNumber, DetailBeneId)
                    : TextNoSeparators(idNumber, DetailBeneId),
                IdTypeCode(row.Source.IdType, idNumber)));
        }

        lines.Add(string.Concat(
            "03", ZeroPad(rows.Count, TrailerCount), ZeroPad(totalSen, TrailerTotal)));

        // CRLF with a trailing newline, and latin1 — BizConverter is a Windows
        // tool and its own output is single-byte.
        var content = Encoding.Latin1.GetBytes(string.Join("\r\n", lines) + "\r\n");
        var fileName = $"CIMB_Payroll_{model.Run.PeriodMonth:D2}{model.Run.PeriodYear}.txt";
        return new StatutoryFileResult(true, fileName, content, ContentType, null);
    }

    // Detail position 127. See UNVERIFIED above — only the NRIC value is
    // corroborated by CIMB's own sample output.
    private static string IdTypeCode(IdType? idType, string idNumber)
    {
        if (idNumber.Length == 0) return " ";
        return idType switch
        {
            IdType.NRIC => "2",
            IdType.PASSPORT => "3",
            IdType.POLICE_NO or IdType.ARMY_NO => "4",
            _ => "2",
        };
    }

    // Plain fixed-width text. Anything outside printable ASCII is dropped —
    // the converter is a Windows/latin1 tool and non-ASCII round-trips badly.
    // Used for the columns the template types as plain "Char".
    private static string Text(string? value, int width)
    {
        var kept = (value ?? string.Empty)
            .Select(c => c is >= ' ' and <= '~' ? c : ' ')
            .ToArray();

        var collapsed = string.Join(" ", new string(kept)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return PayrollBankRows.Clamp(collapsed, width).PadRight(width);
    }

    // The three columns the template types as "Char without '-' or '/'" —
    // Beneficiary Name, Beneficiary ID and Reference Number. Those separators
    // become a space rather than passing through. The company-name column
    // carries no such restriction (CIMB's own sample output contains a
    // hyphen), so it uses Text.
    private static string TextNoSeparators(string? value, int width) =>
        Text((value ?? string.Empty).Replace('-', ' ').Replace('/', ' '), width);

    // Digits only, left-aligned then space-padded — matches the sample.
    private static string DigitsPadded(string? value, int width) =>
        PayrollBankRows.Clamp(StatutoryFileFields.DigitsOnly(value ?? string.Empty), width)
            .PadRight(width);

    // Zero-padded, right-aligned — counts and amounts.
    private static string ZeroPad(long value, int width)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            .PadLeft(width, '0');
        return text.Length <= width ? text : text[^width..];
    }
}
