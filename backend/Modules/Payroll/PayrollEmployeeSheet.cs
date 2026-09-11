using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees;

namespace AltomateHR.Api.Modules.Payroll;

// The columns of the payroll-employee import: the statutory and salary
// details that make someone payable.
//
// Onboarding an org means typing these for every head, and they arrive as a
// spreadsheet from whatever system the org is leaving. This is the same
// shared import machinery Attendance, Leave and Claims use — the reference
// hand-rolls 2,500 lines of bespoke parsing, most of it tolerance this
// codebase already has in `Common/Tabular`.
//
// The identity columns come from `EmployeeImportColumns` so all four
// importers agree about what "who is this row about" means. The import
// UPDATES existing employees; it does not create users, because an account
// carries a login and a role and neither belongs in a payroll sheet.
public static class PayrollEmployeeSheet
{
    public static readonly IReadOnlyList<TabularColumn> ImportColumns =
    [
        new(EmployeeImportColumns.EmailKey, "Employee Email", false, "aisyah@example.com"),
        new(EmployeeImportColumns.NameKey, "Employee Name", false, "Aisyah Binti Rahman"),

        // ---- Identity ----
        new("idNumber", "IC / Passport No", false, "900101-14-5567",
            ["ic", "ic no", "nric", "passport", "personal id"]),
        new("idType", "ID Type", false, "NRIC", ["id type"]),
        new("nationality", "Nationality", false, "Malaysian"),
        new("gender", "Gender", false, "FEMALE"),
        new("maritalStatus", "Marital Status", false, "MARRIED", ["marital status"]),
        new("dateOfBirth", "Date of Birth", false, "1990-01-01", ["dob", "birth date"]),

        // ---- Employment ----
        new("joinDate", "Join Date", false, "2024-03-01", ["date joined", "start date"]),
        new("leaveDate", "Leave Date", false, "", ["date left", "resignation date"]),
        new("department", "Department", false, "Operations"),

        // ---- Salary ----
        new("salaryType", "Salary Type", false, "MONTHLY", ["pay type"]),
        new("monthlySalary", "Monthly Salary", false, "5000.00", ["basic salary", "salary"]),
        new("hourlyRate", "Hourly Rate", false, "25.00"),

        // ---- Statutory ----
        new("epfNumber", "EPF No", false, "7654321", ["kwsp", "kwsp no", "epf"]),
        new("epfEmployeeRate", "EPF Employee Rate %", false, "11", ["epf rate"]),
        new("contributeToEpf", "Contribute to EPF", false, "Yes"),
        new("socsoNumber", "SOCSO No", false, "900101145567", ["perkeso", "socso"]),
        new("contributeToEis", "Contribute to EIS", false, "Yes"),
        new("incomeTaxNumber", "Income Tax No", false, "SG12345678901",
            ["lhdn", "tax no", "pcb no"]),

        // ---- Payment ----
        new("bankName", "Bank Name", false, "Maybank", ["bank"]),
        new("bankAccountNumber", "Bank Account No", false, "112233445566",
            ["account no", "bank account"]),
        new("bankAccountHolderName", "Bank Account Holder", false, "Aisyah Binti Rahman"),
    ];

    // The blank sheet an admin fills in. One example row, which the importer
    // recognises and skips — so a file downloaded and uploaded untouched
    // imports nobody rather than creating a fictional employee.
    public static TabularSheet Template() =>
        TabularTemplate.Build("Payroll employees", ImportColumns);
}
