using AltomateHR.Api.Common.Tabular;

namespace AltomateHR.Api.Modules.Employees;

public interface IEmployeeImportService
{
    TabularExportResult BuildTemplate(TabularFormat format);

    Task<EmployeeImportResult> ImportAsync(byte[] content, TabularFormat format);
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
    // list: they already have a password and the import never touches it.
    public IReadOnlyList<CreatedAccount> CreatedAccounts { get; init; } = [];

    public static EmployeeImportResult FileError(string message) =>
        new() { Ok = false, Message = message };
}

public sealed record CreatedAccount(string Email, string Name, string Password);
