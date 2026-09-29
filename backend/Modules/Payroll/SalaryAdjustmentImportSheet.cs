using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Modules.Payroll;

// The salary adjustment spreadsheet: new salaries for many people at once —
// the annual increment round — each with the date it took effect and why.
//
// Pre-filled with every payroll employee and their current salary, so the
// admin types only the new figures. A row with no New Salary is left alone,
// which is what lets the whole roster ride along in one file.
public static class SalaryAdjustmentImportSheet
{
    public const string SheetName = "Salary adjustments";
    public const string GuideSheetName = "How to fill";

    public const string EmployeeNumberKey = "employeeNumber";
    public const string EmailKey = "email";
    public const string NameKey = "name";
    public const string SalaryTypeKey = "salaryType";
    public const string CurrentSalaryKey = "currentSalary";
    public const string NewSalaryKey = "newSalary";
    public const string EffectiveDateKey = "effectiveDate";
    public const string ReasonKey = "reason";
    public const string NotesKey = "notes";

    // Name, Salary Type and Current Salary are there to read — the import
    // never takes a value from them.
    public static readonly IReadOnlyList<TabularColumn> Columns =
    [
        new(EmployeeNumberKey, "Employee No", false, "", ["employee number", "staff id", "employee id"]),
        new(EmailKey, "Email", false, "", ["employee email"]),
        new(NameKey, "Name", false, "", ["employee name"]),
        new(SalaryTypeKey, "Salary Type", false, "", ["pay type"]),
        new(CurrentSalaryKey, "Current Salary", false, "", ["current basic salary"]),
        new(NewSalaryKey, "New Salary", true, "", ["new basic salary", "new monthly salary"]),
        new(EffectiveDateKey, "Effective Date", false, "", ["effective from", "salary effective date"]),
        new(ReasonKey, "Reason", false, "", ["salary change reason"]),
        new(NotesKey, "Notes", false, "", ["salary change notes"]),
    ];

    // Who a row is about: the employee number, or the email when there is none.
    public static readonly IReadOnlyList<IReadOnlyList<string>> IdentifiedBy = [[EmployeeNumberKey, EmailKey]];

    public sealed record SeedRow(
        string? EmployeeNumber, string Email, string Name, string SalaryType, decimal? CurrentSalary);

    public static TabularSheet Build(IEnumerable<SeedRow> rows)
    {
        var sheet = new TabularSheet(
            SheetName, [.. Columns.Select(c => c.Required ? $"*{c.Label}" : c.Label)]);
        foreach (var r in rows)
        {
            sheet.AddRow(
            [
                r.EmployeeNumber, r.Email, r.Name, r.SalaryType,
                r.CurrentSalary is { } c ? TabularSheet.Money(c) : null,
                null, null, null, null,
            ]);
        }
        return sheet;
    }

    public static TabularSheet Guide()
    {
        var sheet = new TabularSheet(GuideSheetName, ["Column", "What to enter"]) { Filterable = false };
        sheet.AddRow(["Employee No / Email", "Who the row is about. Filled in for you; leave as is."]);
        sheet.AddRow(["Name, Salary Type, Current Salary", "For reading only. The import never changes them."]);
        sheet.AddRow(["New Salary", "The new amount — a monthly salary for MONTHLY staff, an hourly rate for HOURLY staff. Leave blank for anyone whose pay is not changing."]);
        sheet.AddRow(["Effective Date", "YYYY-MM-DD, today or earlier. Blank = today. The new salary applies straight away, so upload on or after the date it takes effect."]);
        sheet.AddRow(["Reason", "RAISE, PROMOTION, DEMOTION, RESTRUCTURE or OTHER. Blank = RAISE."]);
        sheet.AddRow(["Notes", "Optional, e.g. \"Annual review 2026\"."]);
        sheet.AddRow(["", "Each change is recorded in the employee's salary history. If any row has a problem, nothing is saved and every problem is listed."]);
        return sheet;
    }
}
