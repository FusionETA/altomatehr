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

    // One PCB 2(II) per employee, concatenated — the statement of the MTD and
    // CP38 deducted from them. Issued on request at any point in the year, so
    // unlike the others it does not wait for all twelve months.
    PCB2II_BULK_PDF,
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
        string MimeType,
        // False for a statement of what has been deducted SO FAR (PCB 2(II)),
        // which is issued mid-year; the returns declare the whole year.
        bool RequiresFullYear = true);

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
                "Pipe-delimited employer master record. No longer uploaded: since C.P.8D Pin. 2025 "
                + "LHDN takes the employee file only.",
                "LHDN e-CP8D upload", "txt", "text/plain"),

            [PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT] = new(
                PayrollAnnualReportKind.CP8D_EMPLOYEE_TXT,
                GroupLhdnTxt,
                "CP8D employee particulars (P)",
                "Pipe-delimited per-employee rows in LHDN's C.P.8D Pin. 2025 layout (22 fields), "
                + "matching each employee's Form EA. Upload through e-Data Praisi / e-CP8D.",
                "LHDN e-CP8D upload", "txt", "text/plain"),

            [PayrollAnnualReportKind.PCB2II_BULK_PDF] = new(
                PayrollAnnualReportKind.PCB2II_BULK_PDF,
                GroupForms,
                "PCB 2(II) (bulk)",
                "One PCB 2(II) statement per employee: the MTD and CP38 deducted from them in each "
                + "approved month, with LHDN's receipt numbers. Available at any point in the year.",
                null, "pdf", "application/pdf", RequiresFullYear: false),
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
            PayrollAnnualReportKind.PCB2II_BULK_PDF => $"PCB2II_{year}_Bulk.pdf",
            _ => $"payroll-annual-{year}.txt",
        };
    }

    // ─── Shared field rules ─────────────────────────────────────────────

    // The LHDN tax reference as CP8D wants it, with the SG / OG / C prefix and
    // separators stripped — the same rule as the monthly PCB file, which is
    // the previous system's (letters, whitespace and - _ ( ) removed). For a
    // reference of 11 or more digits the LAST digit is the wife code rather
    // than part of the number.
    public static string NormaliseTaxRef(string? taxRef) =>
        StatutoryFileFields.NormaliseTaxRef(taxRef);

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
            // Category 3 needs a definite "spouse works"; unanswered files as
            // 2, as the previous system did.
            return spouseWorking == true ? "3" : "2";
        }

        if (maritalStatus is Employees.Entities.MaritalStatus.DIVORCED
            or Employees.Entities.MaritalStatus.WIDOWED)
        {
            return "3";
        }

        return qualifyingChildren > 0 ? "3" : "1";
    }

    // CP8D field 5, "Status Pekerja". Null when the profile has none, which
    // the file then reports as permanent (Cp8dTxt.DefaultStatus).
    public static int? Cp8dStatus(EmploymentStatus? status) => status switch
    {
        Employees.Entities.EmploymentStatus.MANAGEMENT => 1,
        Employees.Entities.EmploymentStatus.PERMANENT => 2,
        Employees.Entities.EmploymentStatus.CONTRACT => 3,
        Employees.Entities.EmploymentStatus.PART_TIME => 4,
        Employees.Entities.EmploymentStatus.INDUSTRIAL_TRAINEE => 5,
        Employees.Entities.EmploymentStatus.OTHER => 6,
        _ => null,
    };

    // The E-number reduced to the digits LHDN's filenames are built from.
    public static string EmployerNumber(string? employerTin) =>
        StatutoryFileFields.DigitsOnly(employerTin);
}
