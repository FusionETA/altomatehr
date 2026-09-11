using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Modules.Payroll;

// Bulk-filling the statutory and salary details that make someone payable.
//
// UPDATES existing employees only. Creating an account carries a login and a
// role, and neither belongs in a payroll spreadsheet — a row naming someone
// who is not on the roster is reported, not invented.
public interface IPayrollEmployeeImportService
{
    TabularExportResult BuildTemplate(TabularFormat format);

    // The org's current payroll details, in the import's own shape — so an
    // admin can export, edit in a spreadsheet, and import back.
    Task<TabularExportResult> ExportAsync(TabularFormat format);

    Task<TabularImportResult> ImportAsync(byte[] content, TabularFormat format);
}
