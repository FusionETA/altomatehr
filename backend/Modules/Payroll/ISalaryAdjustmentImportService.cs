using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Modules.Payroll;

// New salaries for many employees at once, recorded in each one's salary
// history with its effective date and reason.
public interface ISalaryAdjustmentImportService
{
    // The spreadsheet, pre-filled with every payroll employee's current salary.
    Task<TabularExportResult> BuildTemplateAsync();

    // All or nothing: any problem on any row saves nothing.
    Task<SalaryAdjustmentImportResult> ImportAsync(byte[] content, TabularFormat format);
}

public sealed class SalaryAdjustmentImportResult
{
    public bool Ok { get; init; }

    // A problem with the file itself (unreadable, wrong columns).
    public string? Message { get; init; }

    // Every row problem, so the whole file is fixed in one go.
    public IReadOnlyList<TabularImportError> Errors { get; init; } = [];

    // Recorded in the salary history.
    public int Changed { get; init; }

    // A salary set for the first time — no history entry, as on the screen.
    public int FirstSalaries { get; init; }

    // New Salary equal to the current one.
    public int Unchanged { get; init; }

    public static SalaryAdjustmentImportResult Fail(string message) => new() { Ok = false, Message = message };
}
