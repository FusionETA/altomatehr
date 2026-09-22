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
// the template's column widths. CIMB does not publish the fixed-width spec:
// both documents they do publish — "CIMB BizConverter · Guideline for Bulk
// Payments" and the "Bulk Payroll Payments Guide" — walk through the Excel
// template and the upload screen and never state a field position. The layout
// lives inside the BizConverter executable.
//
// Two positions are still taken on faith, emitted exactly as the sample had
// them, and a real upload is the only way to confirm either:
//
//   • Header 56-71 — sixteen '0's in the sample.
//   • Detail 21-25 — blank in the sample.
//
// A third, detail 127, is now CONSTRAINED rather than guessed. It was modelled
// as an ID-type code on the usual Malaysian convention (1 = old IC, 2 = new
// IC, 3 = passport, 4 = other), but only '2' was ever corroborated — and the
// published guide settles why: the template's Beneficiary ID column takes a
// "Recipient NRIC Number / Business Registration Number" and offers no
// passport option at all. Values 3 and 4 were representing something the
// format does not carry, so a non-NRIC employee is refused by name instead of
// being sent under an invented code. See IdTypeCode.
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

        // CIMB's Beneficiary ID column is an NRIC or a business registration
        // number; the format has no passport. Sending one under an invented
        // type code risks the bank failing that row's beneficiary check — and
        // a row the bank drops is someone who is not paid, which is the exact
        // failure every other refusal in this module exists to prevent.
        var unrepresentable = rows
            .Where(r => r.Source.IdType is not null
                     && r.Source.IdType != IdType.NRIC
                     && !string.IsNullOrWhiteSpace(r.Source.IdNumber))
            .Select(r => $"{r.Source.EmployeeName} ({r.Source.IdType})")
            .ToList();

        if (unrepresentable.Count > 0)
        {
            return StatutoryFileResult.Refused(
                "CIMB's bulk-payroll file identifies each employee by NRIC or business "
                + "registration number, and has no field for any other kind of ID. These are "
                + "on this run: " + string.Join("; ", unrepresentable)
                + ". Pay them through BizChannel separately — the Payment Schedule PDF has "
                + "their account and amount — or confirm the correct code with CIMB before "
                + "including them.");
        }

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
                // "No special character & spacing", per the template's own
                // note on this column.
                DigitsPadded(idNumber, DetailBeneId),
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

    // Detail position 127. Only "2" is corroborated — by CIMB's own sample
    // output, whose three rows all carried a New NRIC.
    //
    // Nothing else can arrive here: a non-NRIC id is refused above, because
    // the format has no field for one. An employee with NO id on file still
    // reaches this, and leaves the position blank exactly as the sample's
    // unfilled cells did.
    private static string IdTypeCode(IdType? idType, string idNumber) =>
        idNumber.Length == 0 ? " " : "2";

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
