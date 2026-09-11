using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Modules.Payroll;

// Seeding a year of payroll history from a spreadsheet.
//
// Why it exists: PCB annualises against the year to date, so an org that
// switches systems in July gets every remaining month's tax wrong unless
// January to June are on file.
public interface IYtdImportService
{
    // A sheet pre-filled with this org's roster, so names match on the way
    // back in.
    Task<TabularExportResult> BuildTemplateAsync(int year, TabularFormat format);

    // What WOULD happen. Writes nothing.
    Task<YtdImportPreview> PreviewAsync(int year, byte[] content, TabularFormat format);

    Task<YtdImportResult> ImportAsync(int year, byte[] content, TabularFormat format);
}

public sealed record YtdImportPreviewRow(
    string EmployeeName,
    string EmployeeProfileId,
    IReadOnlyList<int> Months,
    decimal TotalGross,
    decimal TotalPcb);

public sealed record YtdImportPreview(
    bool Ok,
    IReadOnlyList<string> Errors,
    // Unrecognised columns and months that will be skipped or replaced. Not
    // errors — the import can proceed — but the admin should see them first.
    IReadOnlyList<string> Warnings,
    IReadOnlyList<YtdImportPreviewRow> Employees,
    // Names in the sheet that matched nobody. Surfaced by name so the admin
    // can fix the spelling rather than wonder who was dropped.
    IReadOnlyList<string> UnmatchedNames);

public sealed record YtdImportResult(
    bool Ok,
    IReadOnlyList<string> Errors,
    int MonthsImported,
    int PayslipsImported,
    IReadOnlyList<string> UnmatchedNames,
    // Months left alone because this system already computed them.
    IReadOnlyList<string> SkippedMonths)
{
    public static YtdImportResult Failed(IReadOnlyList<string> errors) =>
        new(false, errors, 0, 0, [], []);
}
