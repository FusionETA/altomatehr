using System.Text;

namespace AltomateHR.Api.Modules.Payroll;

// PCB / MTD TXT — LHDN's CP39 batch submission format.
//
// One header row then one row per employee, fixed width, CRLF.
//
// HEADER (57 chars):
//   001      "H"
//   002-011  HQ employer number      10 numeric, zero-pad
//   012-021  Branch employer number  10 numeric, zero-pad
//   022-025  Deduction year          4 numeric
//   026-027  Deduction month         2 numeric, 01–12
//   028-037  Total PCB, sen          10 numeric, zero-pad
//   038-042  PCB record count        5 numeric, zero-pad
//   043-052  Total CP38, sen         10 numeric, zero-pad
//   053-057  CP38 record count       5 numeric, zero-pad
//
// DETAIL (136 chars):
//   001      "D"
//   002-011  Tax reference           10 numeric, zero-pad, no SG/OG prefix
//   012      Wife code               0, or 1–9 for a jointly-assessed wife
//   013-072  Employee name           60 alphanumeric, left
//   073-084  Old IC                  12 alphanumeric, left — blank
//   085-096  New IC                  12 numeric — blank for a foreigner
//   097-108  Passport number         12 alphanumeric — blank for a local
//   109-110  Country code            2 alpha — blank for a local
//   111-118  PCB amount, sen         8 numeric, zero-pad
//   119-126  CP38 amount, sen        8 numeric, zero-pad
//   127-136  Employee/payroll number 10 alphanumeric, left
//
// Two notes carried over from the reference, both deliberate:
//
//   • Old IC is left BLANK. The code this was ported from wrote the New IC
//     into both slots, which is wrong — an employee with no old-format IC
//     would be filed as having one.
//   • CP38 is a court-ordered arrears instalment and is filed SEPARATELY
//     from PCB. A row with CP38 but no PCB still belongs in the file, which
//     is why the skip test below checks both.
public static class PcbCp39Txt
{
    public const string ContentType = "text/plain";

    private const int HeaderWidth = 57;
    private const int DetailWidth = 136;

    public static StatutoryFileResult Render(StatutoryRunPayload payload)
    {
        var employerNo = StatutoryFileFields.DigitsOnly(payload.CompanyInfo?.EmployerTin);
        if (employerNo.Length == 0)
        {
            return StatutoryFileResult.Refused(
                "Employer LHDN E-number is missing. Set it in Payroll Settings → Company Info "
                + "before generating the PCB file.");
        }

        // A seeded demo value passes the missing check and is caught only by
        // LHDN, which failed a whole submission on "No E (HQ) 1234567890 not
        // exist" — the header, so not one row but the file.
        if (StatutoryFileFields.LooksLikePlaceholderId(employerNo))
        {
            return StatutoryFileResult.Refused(
                $"Employer LHDN E-number \"{payload.CompanyInfo?.EmployerTin}\" looks like a "
                + "placeholder. Enter the real E-number in Payroll Settings → Company Info — it "
                + "goes in the file header, so LHDN rejects the whole submission rather than a row.");
        }

        // No separate HQ field is captured yet. LHDN's guidance for a
        // single-branch employer is that branch and HQ are the same number.
        var hqNo = employerNo;

        var details = new StringBuilder();
        long pcbTotalSen = 0, cp38TotalSen = 0;
        int pcbCount = 0, cp38Count = 0;

        // Collected rather than thrown on the first bad row: an admin fixing
        // nine employees should not need nine downloads to find them.
        var problems = new List<string>();

        foreach (var row in payload.Rows)
        {
            var pcbSen = StatutoryFileFields.ToSen(row.Payslip.Pcb);
            var cp38Sen = StatutoryFileFields.ToSen(row.Payslip.Cp38);

            // Nothing withheld from this person — LHDN does not want the row.
            if (pcbSen <= 0 && cp38Sen <= 0) continue;

            var refusal = Validate(row, out var taxRef, out var newIc, out var passport);
            if (refusal is not null) { problems.Add(refusal); continue; }

            if (pcbSen > 0) { pcbTotalSen += pcbSen; pcbCount++; }
            if (cp38Sen > 0) { cp38TotalSen += cp38Sen; cp38Count++; }

            var detail = new StringBuilder(DetailWidth)
                .Append('D')
                // Positions 2-12 are ONE 11-digit TIN. Splitting it into a
                // 10-digit reference plus a separately-derived wife code is
                // what invented digits: a number stored without its leading
                // zero came out 10 long, kept all ten, and had a guessed code
                // appended. Validate() now requires exactly 11.
                .Append(taxRef)
                .Append(StatutoryFileFields.PadRight(row.EmployeeName, 60))
                .Append(StatutoryFileFields.PadRight(string.Empty, 12))   // Old IC — see above
                .Append(StatutoryFileFields.PadRight(newIc, 12))
                .Append(StatutoryFileFields.PadRight(passport, 12))
                .Append(StatutoryFileFields.PadRight(
                    StatutoryFileFields.CountryCodeForNationality(row.Nationality), 2))
                .Append(StatutoryFileFields.PadZero(pcbSen, 8))
                .Append(StatutoryFileFields.PadZero(cp38Sen, 8))
                .Append(StatutoryFileFields.PadRight(row.EmployeeCode.Trim(), 10));

            details.Append(Exactly(detail.ToString(), DetailWidth))
                   .Append(StatutoryFileFields.LineEnding);
        }

        if (problems.Count > 0)
        {
            return StatutoryFileResult.Refused(
                $"{problems.Count} employee(s) cannot be filed:{StatutoryFileFields.LineEnding}"
                + string.Join(StatutoryFileFields.LineEnding, problems.Select(p => $"• {p}")));
        }

        // The header carries the totals, so it can only be written once the
        // detail rows are known.
        var header = new StringBuilder(HeaderWidth)
            .Append('H')
            .Append(StatutoryFileFields.PadZero(hqNo, 10))
            .Append(StatutoryFileFields.PadZero(employerNo, 10))
            .Append(StatutoryFileFields.PadZero(payload.Run.PeriodYear, 4))
            .Append(payload.Run.PeriodMonth.ToString("D2"))
            .Append(StatutoryFileFields.PadZero(pcbTotalSen, 10))
            .Append(StatutoryFileFields.PadZero(pcbCount, 5))
            .Append(StatutoryFileFields.PadZero(cp38TotalSen, 10))
            .Append(StatutoryFileFields.PadZero(cp38Count, 5));

        var text = Exactly(header.ToString(), HeaderWidth)
                   + StatutoryFileFields.LineEnding
                   + details;

        var fileName = $"pcb-cp39-{payload.Run.PeriodYear}-{payload.Run.PeriodMonth:D2}.txt";
        return StatutoryFileResult.Text(fileName, text, ContentType);
    }

    // Null when the row can be filed. Every message names the person, because
    // the admin's next action is to open that employee's profile.
    //
    // These are checked ONLY for employees who actually have tax withheld —
    // a new joiner whose TIN has not been issued yet does not block the file
    // if nothing was deducted from them.
    private static string? Validate(
        StatutoryEmployeeRow row, out string taxRef, out string newIc, out string passport)
    {
        taxRef = StatutoryFileFields.NormaliseTaxRef(row.IncomeTaxNumber);
        newIc = row.IsLocalOrPr ? StatutoryFileFields.DigitsOnly(row.IdNumber) : string.Empty;
        passport = row.IsLocalOrPr
            ? string.Empty
            : StatutoryFileFields.AlphanumericOnly(row.IdNumber);

        var who = string.IsNullOrWhiteSpace(row.EmployeeCode)
            ? row.EmployeeName
            : $"{row.EmployeeName} ({row.EmployeeCode.Trim()})";

        if (taxRef.Length == 0) return $"{who} has no income tax number.";

        // Exactly 11 — LHDN reads positions 2-12 as one number, so there is
        // nothing to infer and nothing to pad. A shorter value is a TIN saved
        // without its leading zero, which used to be shifted left and topped
        // up with a guessed digit; LHDN answered "Tax Identification Number
        // ... does not exist" on every one.
        if (taxRef.Length != 11)
        {
            return $"{who} has a {taxRef.Length}-digit income tax number ({row.IncomeTaxNumber}); "
                 + "LHDN needs exactly 11 digits, including any leading zero.";
        }

        if (row.IsLocalOrPr && newIc.Length == 0) return $"{who} has no IC number.";

        // A new IC is always 12 digits. A shorter one here is a passport with
        // its letter stripped, which is how a 7-digit "IC" reached LHDN.
        if (row.IsLocalOrPr && newIc.Length != 12)
        {
            return $"{who} has a {newIc.Length}-digit IC number ({row.IdNumber}); a Malaysian IC "
                 + "is 12 digits. If this is a passport, set the ID type to Passport.";
        }

        if (!row.IsLocalOrPr && passport.Length == 0) return $"{who} has no passport number.";
        if (string.IsNullOrWhiteSpace(row.EmployeeCode))
        {
            return $"{row.EmployeeName} has no employee number.";
        }

        return null;
    }

    private static string Exactly(string line, int width) =>
        line.Length == width ? line : line.PadRight(width, ' ')[..width];
}
