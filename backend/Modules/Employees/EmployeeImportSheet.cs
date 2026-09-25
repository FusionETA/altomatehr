using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Modules.Employees;

// The ONE employee spreadsheet: adding people and bulk-updating everything on
// their record — the roster fields Manage Employee shows, the profile, and the
// statutory and salary details payroll needs.
//
// It replaced two sheets (people here, payroll details under Payroll) that
// covered halves of the same person, disagreed about which join date they
// meant, and made an admin juggle two files to onboard one hire.
//
// Email identifies the row and is never rewritten. What a BLANK cell means is
// chosen by the admin at upload time (EmployeeImportBlankCells) — keep the
// existing value, or erase it — and the READ ME sheet says so before anyone
// types a thing.
public static class EmployeeImportSheet
{
    public const string SheetName = "Employees";
    public const string ReadMeSheetName = "READ ME";
    public const string ColumnsSheetName = "Columns";

    // ---- Membership (via IEmployeeService) ----
    public const string EmailKey = "email";
    public const string NameKey = "name";
    public const string RoleKey = "role";
    public const string EmployeeNumberKey = "employeeNumber";
    public const string JobTitleKey = "jobTitle";
    public const string JoinDateKey = "joinDate";
    public const string DateOfBirthKey = "dateOfBirth";
    public const string PolicyKey = "policy";
    public const string ShiftKey = "shift";

    // A value these must always have. In "erase" mode a blank here fails the
    // row instead of clearing it: nobody can be nameless, roleless, without the
    // employee number payroll files by, or without an EPF/EIS decision.
    public static readonly IReadOnlySet<string> CannotBeBlank = new HashSet<string>(StringComparer.Ordinal)
    {
        NameKey, RoleKey, EmployeeNumberKey, "salaryType", "contributeToEpf", "contributeToEis",
    };

    // Column → one line of help for the Columns sheet. Kept beside the column
    // list so a new column cannot ship without saying what goes in it.
    private static readonly List<(TabularColumn Column, string Help)> Definitions =
    [
        // ---- Who ----
        (new(EmailKey, "Email", true, "aisyah@example.com", ["employee email"]),
            "Identifies the person. Never changed by an import. A new email adds a new person."),
        (new(NameKey, "Name", false, "Aisyah Binti Rahman", ["employee name", "full name"]),
            "Full name. Needed for a new person."),
        (new(RoleKey, "Role", false, "Employee", ["access"]),
            "Employee, Supervisor, Admin or Owner. A new person defaults to Employee."),
        (new(EmployeeNumberKey, "Employee No", false, "EMP-001", ["employee number", "staff id", "employee id"]),
            "Needed for a new person — payroll files by it."),
        (new(JobTitleKey, "Job Title", false, "Site Engineer", ["position"]),
            "Free text."),
        (new(JoinDateKey, "Join Date", false, "2026-01-15", ["start date", "date joined"]),
            "YYYY-MM-DD. Drives pro-rated leave and payroll."),
        (new(DateOfBirthKey, "Date of Birth", false, "1990-11-23", ["dob", "birth date", "birthday"]),
            "YYYY-MM-DD. Needed for a new person: their first password is their email followed by their birthday as MMDD."),
        (new(PolicyKey, "Policy", false, "Full-time", ["leave policy"]),
            "The name of one of your policies. Blank = the company's default policy."),
        (new(ShiftKey, "Shift", false, "Office Hours", ["work shift"]),
            "The name of one of your shifts. Blank = the project's default shift."),

        // ---- Personal ----
        (new("idNumber", "IC / Passport No", false, "900101-14-5567", ["ic", "ic no", "nric", "passport", "personal id"]),
            "Free text."),
        (new("idType", "ID Type", false, "NRIC", ["id type"]),
            "NRIC, PASSPORT, ARMY_NO or POLICE_NO."),
        (new("nationality", "Nationality", false, "Malaysian"), "Free text."),
        (new("gender", "Gender", false, "FEMALE"), "MALE or FEMALE."),
        (new("race", "Race", false, "Malay"), "Free text."),
        (new("maritalStatus", "Marital Status", false, "MARRIED", ["marital status"]),
            "SINGLE, MARRIED, DIVORCED or WIDOWED."),
        (new("hasPr", "Malaysian PR", false, "No", ["pr", "permanent resident", "has pr"]),
            "Yes or No. Decides local vs foreign-worker payroll rules."),
        (new("isResident", "Tax Resident", false, "Yes", ["resident", "is resident"]),
            "Yes or No."),
        (new("isOku", "OKU", false, "No", ["disabled", "is oku"]), "Yes or No."),

        // ---- Contact & address ----
        (new("phone", "Phone", false, "012-345 6789", ["mobile", "phone no"]), "Free text."),
        (new("alternateEmail", "Alternate Email", false, "aisyah.personal@example.com", ["personal email"]),
            "Free text. Not used to sign in."),
        (new("addressLine1", "Address Line 1", false, "12 Jalan Mawar", ["address"]), "Free text."),
        (new("addressLine2", "Address Line 2", false, "Taman Melati"), "Free text."),
        (new("city", "City", false, "Kuala Lumpur"), "Free text."),
        (new("postcode", "Postcode", false, "53100", ["postal code", "zip"]), "Free text."),
        (new("state", "State", false, "Wilayah Persekutuan"), "Free text."),

        // ---- Emergency contact ----
        (new("emergencyContactName", "Emergency Contact Name", false, "Rahman Bin Ali"), "Free text."),
        (new("emergencyContactPhone", "Emergency Contact Phone", false, "013-456 7890"), "Free text."),
        (new("emergencyContactRelation", "Emergency Contact Relationship", false, "Father",
            ["emergency contact relation"]), "Free text."),

        // ---- Employment ----
        (new("leaveDate", "Leave Date", false, "", ["date left", "resignation date"]),
            "YYYY-MM-DD, once they have left."),
        (new("department", "Department", false, "Operations"), "Free text."),
        (new("location", "Location", false, "HQ"), "Free text."),
        (new("workSchedule", "Work Schedule", false, "Mon-Fri"), "Free text."),

        // ---- Spouse ----
        (new("spouseWorking", "Spouse Working", false, "", ["spouse working"]),
            "Yes or No. Needed for married employees (PCB spouse relief)."),
        (new("spouseDisabled", "Spouse Disabled", false, "", ["spouse disabled"]), "Yes or No."),
        (new("spouseIdNumber", "Spouse IC / Passport No", false, "", ["spouse ic"]), "Free text."),
        (new("spousePcbNumber", "Spouse Income Tax No", false, "", ["spouse tax no", "spouse pcb no"]),
            "Free text."),

        // ---- Salary ----
        (new("salaryType", "Salary Type", false, "MONTHLY", ["pay type"]), "MONTHLY or HOURLY."),
        (new("monthlySalary", "Monthly Salary", false, "5000.00", ["basic salary", "salary"]),
            "Amount, e.g. 5000.00."),
        (new("hourlyRate", "Hourly Rate", false, "25.00"), "Amount, e.g. 25.00."),

        // ---- Statutory ----
        (new("epfNumber", "EPF No", false, "7654321", ["kwsp", "kwsp no", "epf"]), "Free text."),
        (new("epfEmployeeRate", "EPF Employee Rate %", false, "11", ["epf rate"]),
            "A percentage. Blank or 0 = the statutory rate."),
        (new("contributeToEpf", "Contribute to EPF", false, "Yes"), "Yes or No."),
        (new("epfEmployeeVoluntary", "EPF Employee Voluntary %", false, "0", ["epf employee voluntary"]),
            "A percentage on top of the statutory rate."),
        (new("epfEmployerVoluntary", "EPF Employer Voluntary %", false, "0", ["epf employer voluntary"]),
            "A percentage on top of the statutory rate."),
        (new("epfMemberBefore1998", "EPF Member Before 1998", false, "No"), "Yes or No."),
        (new("socsoNumber", "SOCSO No", false, "900101145567", ["perkeso", "socso"]), "Free text."),
        (new("socsoScheme", "SOCSO Scheme", false, "EMPLOYMENT_INJURY_INVALIDITY", ["socso scheme", "perkeso scheme"]),
            "EMPLOYMENT_INJURY_INVALIDITY or EMPLOYMENT_INJURY_ONLY."),
        (new("contributeToEis", "Contribute to EIS", false, "Yes"), "Yes or No."),
        (new("contributeToSkbbk", "Contribute to SKBBK", false, "No"), "Yes or No."),
        (new("incomeTaxNumber", "Income Tax No", false, "SG12345678901", ["lhdn", "tax no", "pcb no"]),
            "Free text."),
        (new("pcbBorneByEmployer", "PCB Borne by Employer", false, "No"), "Yes or No."),

        // ---- Payment ----
        (new("paymentMethod", "Payment Method", false, "BANK_TRANSFER"), "BANK_TRANSFER, CASH or CHEQUE."),
        (new("bankName", "Bank Name", false, "Maybank", ["bank"]), "Free text."),
        (new("bankAccountNumber", "Bank Account No", false, "112233445566", ["account no", "bank account"]),
            "Free text."),
        (new("bankAccountHolderName", "Bank Account Holder", false, "Aisyah Binti Rahman"), "Free text."),
    ];

    public static readonly IReadOnlyList<TabularColumn> Columns = [.. Definitions.Select(d => d.Column)];

    // The Employees sheet, then the guides. The template leads with READ ME so
    // Excel opens on the warning; the import finds the data by sheet name.
    public static IReadOnlyList<TabularSheet> Workbook(TabularSheet employees) =>
        [ReadMe(), employees, ColumnGuide()];

    public static TabularSheet BuildTemplate() =>
        TabularTemplate.Build(SheetName, Columns);

    // Plain sentences, one per row, under a header that is itself the warning
    // — the first thing anyone sees on opening the file.
    public static TabularSheet ReadMe()
    {
        var sheet = new TabularSheet(ReadMeSheetName, ["READ THIS BEFORE IMPORTING"]) { Filterable = false };

        sheet.AddRow("HOW THIS FILE WORKS");
        sheet.AddRow("1. Fill in the Employees sheet — one row per person. Email identifies them and is never changed by an import.");
        sheet.AddRow("2. A new email adds that person. They need Name, Employee No and Date of Birth (their first password is their email followed by their birthday as MMDD).");
        sheet.AddRow("3. An email that's already in the company updates that person. Filled cells overwrite what's there now.");
        sheet.AddRow("4. To update everyone in bulk: Export current employees, edit the cells you want, then import the same file.");
        sheet.AddRow("");

        sheet.AddRow("⚠ BLANK CELLS — you choose what they mean when you upload the file:");
        sheet.EmphasizeLastRow();
        sheet.AddRow("• Keep existing values: a blank cell leaves that field as it is.");
        sheet.AddRow("• Erase existing values: a blank cell ERASES that field for that person.");
        sheet.EmphasizeLastRow();
        sheet.AddRow("   Name, Role, Employee No, Salary Type, Contribute to EPF and Contribute to EIS can't be erased — a blank there fails that row.");
        sheet.AddRow("   A blank Yes/No resets to its default (No; Tax Resident: Yes), a blank EPF rate uses the statutory rate, a blank Payment Method is Bank transfer.");
        sheet.AddRow("• Either way, a column you delete from the sheet is left alone for everyone, and people not in the file are not changed or removed.");
        sheet.AddRow("");

        sheet.AddRow("GOOD TO KNOW");
        sheet.AddRow("• A row with a mistake is skipped and listed with its row number. Every other row still imports.");
        sheet.AddRow("• The template's example row is recognised and skipped — leave it, or delete it.");
        sheet.AddRow("• Dates are YYYY-MM-DD. Yes/No columns take Yes or No. The Columns sheet lists what every column accepts.");
        sheet.AddRow("• This file holds personal and bank details. Keep it somewhere safe and delete it when you're done.");
        return sheet;
    }

    public static TabularSheet ColumnGuide()
    {
        var sheet = new TabularSheet(ColumnsSheetName, ["Column", "Required", "What to enter"]) { Filterable = false };
        foreach (var (column, help) in Definitions)
        {
            var required = column.Key == EmailKey
                ? "Always"
                : column.Key is NameKey or EmployeeNumberKey or DateOfBirthKey
                    ? "New people"
                    : CannotBeBlank.Contains(column.Key) ? "Can't be erased" : "";
            sheet.AddRow(column.Label, required, help);
        }
        return sheet;
    }
}
