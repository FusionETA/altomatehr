using System.Globalization;
using System.Text;

namespace AltomateHR.Api.Modules.Payroll;

// The two pipe-delimited files LHDN's e-CP8D upload takes: an employer master
// record (M) and the per-employee particulars (P).
//
// Pure functions over `PayrollAnnualPayload`. Unlike the monthly submission
// files these are delimited rather than fixed-width, so a short field does not
// shift the ones after it — but the COLUMN COUNT and their order are still the
// contract, and a missing column silently re-reads every later value as the
// wrong field. The tests therefore assert on split positions, not substrings.
public static class Cp8dTxt
{
    // LHDN's parsers are Windows-era and reject a file whose last line has no
    // terminator, so every row ends CRLF including the final one.
    private const string LineEnd = "\r\n";

    // ─── M — the employer master ────────────────────────────────────────

    // One line: {employerNo}|{employerName}|{year}
    public static StatutoryFileResult RenderEmployer(PayrollAnnualPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.EmployerNo))
        {
            return StatutoryFileResult.Refused(
                "The employer's LHDN E-number is missing. Set it under Payroll Settings → "
                + "Company Info before generating the CP8D upload files.");
        }

        var employerName = (payload.CompanyInfo?.EmployerName ?? payload.OrganizationName)
            .Trim().ToUpperInvariant();

        var line = $"{payload.EmployerNo}|{employerName}|{payload.Year}{LineEnd}";

        return Ok(PayrollAnnualReportKind.CP8D_EMPLOYER_TXT, payload, line);
    }

    // ─── P — the employee particulars ───────────────────────────────────

    // LHDN's "Susun Atur Maklumat C.P.8D - Pin. 2025": twenty-two pipe-
    // separated fields per employee, plus a trailing pipe. Money comes from the
    // same line-by-line figures as Form EA (FormEaLines), so an employee's EA
    // and their CP8D row can never disagree.
    //
    //    1  Name, uppercase                                    (mandatory)
    //    2  Income tax number (TIN), digits only; blank if none
    //    3  New IC (digits) — or the passport as written; 000000000000 if none (mandatory)
    //    4  Tax category 1 / 2 / 3                             (mandatory)
    //    5  Employee status 1–6                                (mandatory)
    //    6  Retirement / contract-end date, dd-MM-yyyy; the cessation date for
    //       someone who left in the year                       (mandatory)
    //    7  Tax borne by employer: 1 = yes, 2 = no             (mandatory)
    //    8  Qualifying children
    //    9  Child relief, whole ringgit
    //   10  Gross remuneration, whole ringgit (EA B, less 11–13)
    //   11  Benefits in kind          12  Living accommodation
    //   13  ESOS                      14  Tax-exempt allowances / perquisites (EA F)
    //   15  TP1 relief claims (whole ringgit)
    //   16  TP1 zakat, other than via salary (with sen)
    //   17  EPF, employee share (whole ringgit)
    //   18  Zakat via salary deduction (with sen)
    //   19  PCB — all MTD remitted (with sen)
    //   20  CP38 (with sen)
    //   21  Medical insurance via salary deduction — not recorded, left blank
    //   22  PERKESO via salary deduction: SOCSO (with SKBBK) and EIS
    //
    // An optional amount that is nil is left empty, as LHDN's own example
    // does; EPF and PCB are always written.
    public static StatutoryFileResult RenderEmployees(PayrollAnnualPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.EmployerNo))
        {
            return StatutoryFileResult.Refused(
                "The employer's LHDN E-number is missing. Set it under Payroll Settings → "
                + "Company Info before generating the CP8D upload files.");
        }

        var builder = new StringBuilder();

        foreach (var employee in payload.Employees)
        {
            // Somebody with neither a tax reference nor any PCB withheld has
            // nothing for LHDN to match against and nothing to report. Filing
            // an empty row for them invites a rejection on a record that
            // should not have been sent.
            // Empty, not blank: a whitespace-only reference is still filed, as
            // the previous system filed it.
            if (string.IsNullOrEmpty(employee.IncomeTaxNumber) && employee.TotalMtdRemitted == 0m)
            {
                continue;
            }

            var ea = employee.Ea;
            string[] columns =
            [
                employee.EmployeeName.ToUpperInvariant(),
                PayrollAnnualReports.NormaliseTaxRef(employee.IncomeTaxNumber),
                IdentityNumber(employee),
                employee.Cp8dCategoryOverride
                    ?? PayrollAnnualReports.TaxCategory(
                        employee.MaritalStatus, employee.SpouseWorking, employee.QualifyingChildren),
                (employee.Cp8dStatusOverride ?? DefaultStatus).ToString(CultureInfo.InvariantCulture),
                RetirementOrCessation(employee, payload.Year),
                employee.PcbBorneByEmployer ? "1" : "2",
                employee.QualifyingChildren.ToString(CultureInfo.InvariantCulture),
                Ringgit(employee.AnnualChildRelief),
                Ringgit(ea.Cp8dGross),
                OptionalRinggit(ea.B3),
                OptionalRinggit(ea.B4),
                OptionalRinggit(ea.B1e),
                OptionalRinggit(ea.F),
                OptionalRinggit(ea.D5aTp1Relief),
                OptionalSen(ea.D5bZakatSelfPaid),
                Ringgit(employee.TotalEpfEmployee),
                OptionalSen(ea.D3ZakatViaSalary),
                Sen(employee.TotalMtdRemitted),
                OptionalSen(employee.TotalCp38),
                string.Empty,
                OptionalRinggit(employee.TotalSocsoEmployee + employee.TotalEisEmployee),
            ];

            builder.Append(string.Join('|', columns)).Append('|').Append(LineEnd);
        }

        return Ok(PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT, payload, builder.ToString());
    }

    // Field 5 — "Tetap" (permanent) unless recorded otherwise. The profile has
    // no employment status yet; the converter can set one per row.
    public const int DefaultStatus = 2;

    // Malaysia's statutory minimum retirement age (Minimum Retirement Age Act
    // 2012), for field 6 when no contract end is recorded.
    public const int RetirementAge = 60;

    // Field 3: the new IC as 12 digits; a passport (or police / army number)
    // as written — stripping it to digits would file someone else's number.
    // LHDN asks for twelve zeros when there is no identity number at all.
    private static string IdentityNumber(AnnualEmployeeRow e)
    {
        var id = (e.IdNumber ?? string.Empty).Trim();
        if (id.Length == 0) return "000000000000";
        return e.IdType is null or Employees.Entities.IdType.NRIC
            ? StatutoryFileFields.DigitsOnly(id)
            : new string([.. id.ToUpperInvariant().Where(char.IsLetterOrDigit)]);
    }

    // Field 6: a cessation in the year wins (LHDN: "isi tarikh pemberhentian
    // pada tahun saraan"); otherwise the contract end as typed; otherwise the
    // day the employee reaches the retirement age. Blank only when there is no
    // date of birth to work from.
    private static string RetirementOrCessation(AnnualEmployeeRow e, int year)
    {
        DateTime? date = e.LeaveDate is { } left && left.Year <= year
            ? left
            : e.Cp8dRetirementDateOverride
              ?? e.DateOfBirth?.AddYears(RetirementAge);
        return date?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    // The amount columns are whole ringgit. Rounded rather than truncated:
    // this is a declaration of income, and rounding down every employee would
    // under-declare the employer's total.
    private static string Ringgit(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);

    private static string OptionalRinggit(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero) == 0m ? string.Empty : Ringgit(amount);

    private static string Sen(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string OptionalSen(decimal amount) => amount == 0m ? string.Empty : Sen(amount);

    private static StatutoryFileResult Ok(
        PayrollAnnualReportKind kind, PayrollAnnualPayload payload, string content)
    {
        var meta = PayrollAnnualReports.All[kind];

        return new StatutoryFileResult(
            true,
            PayrollAnnualReports.FileName(kind, payload.Year, payload.EmployerNo),
            // UTF-8 without a BOM, as the previous system wrote it: a byte-order
            // mark at the head of the first field is read as part of the
            // employer number, and ASCII would turn an accented name into "?".
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content),
            meta.MimeType,
            null);
    }
}
