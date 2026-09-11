using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The year-end filings, and the small shared rules the four of them agree on.
public enum PayrollAnnualReportKind
{
    // One EA per employee, concatenated. The employer hands these out by
    // 28 February of the following year.
    FORM_EA_BULK_PDF,

    // The employer's own return, with the CP8D schedule behind it.
    FORM_E_CP8D_PDF,

    // The two pipe-delimited files LHDN's e-CP8D upload expects.
    CP8D_EMPLOYER_TXT,
    CP8D_EMPLOYEE_TXT,
}

public static class PayrollAnnualReports
{
    public sealed record Meta(
        PayrollAnnualReportKind Kind,
        string Group,
        string Title,
        string Description,
        // Where the file is uploaded, when it is uploaded anywhere. Null for
        // the documents an employer keeps or distributes itself.
        string? Portal,
        string Extension,
        string MimeType);

    public const string GroupForms = "FORMS";
    public const string GroupLhdnTxt = "LHDN_TXT";

    public static readonly IReadOnlyDictionary<PayrollAnnualReportKind, Meta> All =
        new Dictionary<PayrollAnnualReportKind, Meta>
        {
            [PayrollAnnualReportKind.FORM_EA_BULK_PDF] = new(
                PayrollAnnualReportKind.FORM_EA_BULK_PDF,
                GroupForms,
                "Form EA (bulk)",
                "One EA form per employee, concatenated. Distribute to employees by 28 February "
                + "of the following year.",
                null, "pdf", "application/pdf"),

            [PayrollAnnualReportKind.FORM_E_CP8D_PDF] = new(
                PayrollAnnualReportKind.FORM_E_CP8D_PDF,
                GroupForms,
                "Form E + CP8D",
                "The employer's annual return, with the CP8D schedule of per-employee particulars "
                + "behind it.",
                null, "pdf", "application/pdf"),

            [PayrollAnnualReportKind.CP8D_EMPLOYER_TXT] = new(
                PayrollAnnualReportKind.CP8D_EMPLOYER_TXT,
                GroupLhdnTxt,
                "CP8D employer master (M)",
                "Pipe-delimited employer master record.",
                "LHDN e-CP8D upload", "txt", "text/plain"),

            [PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT] = new(
                PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT,
                GroupLhdnTxt,
                "CP8D employee particulars (P)",
                "Pipe-delimited per-employee rows.",
                "LHDN e-CP8D upload", "txt", "text/plain"),
        };

    // LHDN's own filename convention for the upload pair: the employer number
    // prefixed M or P. An admin with several orgs open can tell downloaded
    // files apart by nothing else.
    public static string FileName(PayrollAnnualReportKind kind, int year, string employerNo)
    {
        var stem = string.IsNullOrWhiteSpace(employerNo) ? "EMPLOYER" : employerNo;

        return kind switch
        {
            PayrollAnnualReportKind.FORM_EA_BULK_PDF => $"Form_EA_{year}_Bulk.pdf",
            PayrollAnnualReportKind.FORM_E_CP8D_PDF => $"Form_E_CP8D_{year}.pdf",
            PayrollAnnualReportKind.CP8D_EMPLOYER_TXT => $"M{stem}_{year}.TXT",
            PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT => $"P{stem}_{year}.TXT",
            _ => $"payroll-annual-{year}.txt",
        };
    }

    // ─── Shared field rules ─────────────────────────────────────────────

    // The LHDN tax reference as CP8D wants it: digits only. The SG / OG / C
    // prefix and any punctuation are stripped, and for a reference of 11 or
    // more digits the LAST digit is the wife code rather than part of the
    // number.
    public static string NormaliseTaxRef(string? taxRef) =>
        string.IsNullOrWhiteSpace(taxRef)
            ? string.Empty
            : new string([.. taxRef.Where(char.IsDigit)]);

    // A new IC as 12 digits with no dashes. A passport is NOT an IC and is
    // deliberately not coerced into one — CP8D has its own handling, and
    // padding a passport number to look like an IC would file a wrong one.
    public static string NormaliseNewIc(string? idNumber, IdType? idType)
    {
        if (idType is not null && idType != Employees.Entities.IdType.NRIC) return string.Empty;
        if (string.IsNullOrWhiteSpace(idNumber)) return string.Empty;

        return new string([.. idNumber.Where(char.IsDigit)]);
    }

    // CP8D column 4. LHDN's three categories:
    //   1 — single, no qualifying children
    //   2 — married with a spouse who has no income; the household's relief
    //       is claimed on one return
    //   3 — married with a working spouse, or divorced / widowed /
    //       separated, or single but claiming a child
    public static string TaxCategory(
        MaritalStatus? maritalStatus, bool? spouseWorking, int qualifyingChildren)
    {
        if (maritalStatus == Employees.Entities.MaritalStatus.MARRIED)
        {
            // Only a definite "spouse does not work" opens category 2 — the
            // same gate the PCB spouse relief uses.
            return spouseWorking == false ? "2" : "3";
        }

        if (maritalStatus is Employees.Entities.MaritalStatus.DIVORCED
            or Employees.Entities.MaritalStatus.WIDOWED)
        {
            return "3";
        }

        return qualifyingChildren > 0 ? "3" : "1";
    }

    // The E-number reduced to the digits LHDN's filenames are built from.
    public static string EmployerNumber(string? employerTin) => NormaliseTaxRef(employerTin);
}
