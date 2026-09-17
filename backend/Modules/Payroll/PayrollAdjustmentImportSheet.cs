using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The bulk-adjustment spreadsheet: one row per manual line an admin wants on a
// DRAFT run, plus an optional salary change for the same person.
//
// Two sheets. The first is what gets filled in and read back; the second is a
// read-only catalogue of the categories, because "Category" is a coded value
// and an admin guessing at it gets their row rejected.
//
// ⚠️ The two halves of a row have OPPOSITE blank semantics, which is the one
// thing about this file that can cost real money:
//
//   · adjustments  REPLACE. Importing wipes every manual line already on the
//     run and rebuilds from the file, so a line left out is a line deleted.
//     That is why the template arrives pre-filled with what is already there.
//
//   · the salary columns do NOT. Blank means "leave this salary alone".
//     Replace semantics there would reset everyone's pay to whatever the
//     spreadsheet happened to contain.
public static class PayrollAdjustmentImportSheet
{
    public const string SheetName = "Adjustments";
    public const string CategoriesSheetName = "Categories";

    public const string NameKey = "name";
    public const string CategoryKey = "category";
    public const string LabelKey = "label";
    public const string AmountKey = "amount";
    public const string RecurringKey = "recurring";
    public const string CurrentSalaryKey = "currentSalary";
    public const string NewSalaryKey = "newSalary";
    public const string ReasonKey = "salaryReason";
    public const string EffectiveKey = "salaryEffective";
    public const string SalaryNotesKey = "salaryNotes";

    public static readonly IReadOnlyList<TabularColumn> Columns =
    [
        new(NameKey, "Full Name", true, "Aisyah Binti Rahman", ["employee", "employee name"]),
        new(CategoryKey, "Category", false, "allowance_travel", ["code"]),
        new(LabelKey, "Label", false, "February site travel"),
        new(AmountKey, "Amount", false, "250.00"),
        new(RecurringKey, "Treat as recurring", false, "No", ["recurring"]),

        // Reference only — never read back. It is here so the admin can see
        // what they are changing FROM before they type what to change it to.
        new(CurrentSalaryKey, "Current Salary (reference)", false, "5000.00"),

        // Blank = no change. See the warning above.
        new(NewSalaryKey, "New Basic Salary", false, "", ["basic salary", "new salary"]),
        new(ReasonKey, "Salary Change Reason", false, "RAISE", ["reason"]),
        new(EffectiveKey, "Salary Effective Date", false, "", ["effective date"]),
        new(SalaryNotesKey, "Salary Change Notes", false, "", ["salary notes"]),
    ];

    // The catalogue, so the Category column can be filled from a list rather
    // than from memory. Mirrors the columns the adjustment editor shows, since
    // the decision an admin is making is the same one.
    public static TabularSheet BuildCategories()
    {
        var sheet = new TabularSheet(
            CategoriesSheetName,
            ["Category", "Label", "Kind", "Adds to", "EPF", "SOCSO", "EIS", "PCB"],
            "Copy a Category value into the Adjustments sheet. Read-only.");

        foreach (var (code, meta) in PayrollAdjustmentCategories.All.OrderBy(
                     p => p.Value.Kind.ToString() + p.Value.Label, StringComparer.OrdinalIgnoreCase))
        {
            sheet.AddRow(
                code,
                meta.Label,
                meta.Kind.ToString(),
                meta.ReducesBase ? "Deduction" : "Addition",
                TabularSheet.Bool(meta.SubjectToEpf),
                TabularSheet.Bool(meta.SubjectToSocso),
                TabularSheet.Bool(meta.SubjectToEis),
                TabularSheet.Bool(meta.SubjectToPcb));
        }

        return sheet;
    }

    // One row per existing manual line, plus one blank row per employee who has
    // none — so the file is both a record of what is on the run and a form for
    // what should be. Without the existing lines an admin would silently delete
    // them by uploading, given the replace semantics.
    public static TabularSheet BuildTemplate(IReadOnlyList<AdjustmentImportSeedRow> seed)
    {
        var headers = Columns.Select(c => c.Required ? $"*{c.Label}" : c.Label).ToList();
        var sheet = new TabularSheet(
            SheetName,
            headers,
            "Manual lines REPLACE what is on the run — a line deleted here is deleted there. "
            + "Salary columns are the exception: blank leaves the salary alone.");

        foreach (var row in seed)
        {
            sheet.AddRow(
                row.Name,
                row.Category ?? string.Empty,
                row.LineLabel ?? string.Empty,
                row.Amount is null ? string.Empty : TabularSheet.Money(row.Amount.Value),
                row.Category is null ? string.Empty : TabularSheet.Bool(row.TreatAsRecurring),
                row.CurrentSalary is null ? string.Empty : TabularSheet.Money(row.CurrentSalary.Value),
                // The four salary columns ship EMPTY on purpose. Pre-filling the
                // new salary with the current one would make every upload look
                // like a company-wide salary change.
                string.Empty, string.Empty, string.Empty, string.Empty);
        }

        return sheet;
    }
}

// One line of the pre-filled template: an employee, and the manual line they
// already have (if any).
public sealed record AdjustmentImportSeedRow(
    string Name,
    decimal? CurrentSalary,
    string? Category = null,
    string? LineLabel = null,
    decimal? Amount = null,
    bool TreatAsRecurring = false);

// What one parsed row asks for. Either half may be absent: a row can carry a
// manual line, a salary change, or both.
public sealed record ParsedAdjustmentRow(
    int RowNumber,
    string Name,
    string? Category,
    string? Label,
    decimal Amount,
    bool TreatAsRecurring,
    decimal? NewSalary,
    SalaryChangeReason Reason,
    DateTime? EffectiveDate,
    string? SalaryNotes)
{
    public bool HasLine => Category is not null;
    public bool HasSalaryChange => NewSalary is not null;
}
