using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Modules.Employees;

public interface IEmployeeImportService
{
    TabularExportResult BuildTemplate(TabularFormat format);

    // The current roster in exactly the import's column order, so an admin
    // edits what is there instead of retyping it. Without this the import can
    // only CREATE people — filling in an employee number for thirty existing
    // ones would mean typing thirty rows from scratch.
    Task<TabularExportResult> ExportAsync(TabularFormat format);

    // `blanks` is the admin's choice at upload: what an empty cell does to
    // someone who already exists. A column missing from the file is left alone
    // either way.
    Task<EmployeeImportResult> ImportAsync(
        byte[] content, TabularFormat format, EmployeeImportBlankCells blanks = EmployeeImportBlankCells.Keep);
}

public enum EmployeeImportBlankCells
{
    // A blank cell leaves the existing value as it is. The safe default: a
    // partial sheet ("just the bank details") can't wipe anything.
    Keep,

    // A blank cell erases the existing value — the sheet is the truth. Fields
    // that must always hold a value fail the row instead (CannotBeBlank).
    Erase,
}

public sealed class EmployeeImportResult
{
    public bool Ok { get; init; } = true;

    // A whole-file problem: unreadable, empty, a missing column.
    public string? Message { get; init; }

    public int Created { get; init; }
    public int Updated { get; init; }

    public IReadOnlyList<TabularImportError> Errors { get; init; } = [];

    // The accounts this import brought into being, with the password each was
    // given. Returned ONCE, in this response, and stored nowhere — the admin
    // hands them out and they are gone. Existing people are absent from this
    // list, as is anyone whose account already existed in another company:
    // both keep the password they have, and the import never touches it.
    public IReadOnlyList<CreatedAccount> CreatedAccounts { get; init; } = [];

    public static EmployeeImportResult FileError(string message) =>
        new() { Ok = false, Message = message };
}

public sealed record CreatedAccount(string Email, string Name, string Password);
