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

    // Sixteen pipe-separated columns per employee, plus a trailing pipe:
    //
    //    1  Name, uppercase, as on the IC or passport
    //    2  Income tax reference, digits only
    //    3  New IC, 12 digits, no dashes
    //    4  Tax category (1 / 2 / 3)
    //    5  Tax borne by employer (1 = yes, 2 = no)
    //    6  Qualifying children
    //    7  Annual child relief, whole ringgit
    //    8  Annual gross remuneration, whole ringgit
    //  9–13  Reserved by LHDN, left empty
    //   14  EPF employee contribution, whole ringgit
    //   15  Reserved, left empty
    //   16  PCB, to two decimals
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
            if (string.IsNullOrWhiteSpace(employee.IncomeTaxNumber) && employee.TotalPcb == 0m)
            {
                continue;
            }

            string[] columns =
            [
                employee.EmployeeName.ToUpperInvariant(),
                PayrollAnnualReports.NormaliseTaxRef(employee.IncomeTaxNumber),
                PayrollAnnualReports.NormaliseNewIc(employee.IdNumber, employee.IdType),
                PayrollAnnualReports.TaxCategory(
                    employee.MaritalStatus, employee.SpouseWorking, employee.QualifyingChildren),
                employee.PcbBorneByEmployer ? "1" : "2",
                employee.QualifyingChildren.ToString(CultureInfo.InvariantCulture),
                Ringgit(employee.AnnualChildRelief),
                Ringgit(employee.TotalIncome),
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                Ringgit(employee.TotalEpfEmployee),
                string.Empty,
                employee.TotalPcb.ToString("0.00", CultureInfo.InvariantCulture),
            ];

            builder.Append(string.Join('|', columns)).Append('|').Append(LineEnd);
        }

        return Ok(PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT, payload, builder.ToString());
    }

    // The amount columns are whole ringgit. Rounded rather than truncated:
    // this is a declaration of income, and rounding down every employee would
    // under-declare the employer's total.
    private static string Ringgit(decimal amount) =>
        Math.Round(amount, 0, MidpointRounding.AwayFromZero)
            .ToString("0", CultureInfo.InvariantCulture);

    private static StatutoryFileResult Ok(
        PayrollAnnualReportKind kind, PayrollAnnualPayload payload, string content)
    {
        var meta = PayrollAnnualReports.All[kind];

        return new StatutoryFileResult(
            true,
            PayrollAnnualReports.FileName(kind, payload.Year, payload.EmployerNo),
            // ASCII, not UTF-8 with a BOM: a byte-order mark at the head of
            // the first field is read as part of the employer number.
            Encoding.ASCII.GetBytes(content),
            meta.MimeType,
            null);
    }
}
