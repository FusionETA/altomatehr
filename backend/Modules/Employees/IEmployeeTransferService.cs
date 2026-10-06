using AltomateHR.Api.Modules.Employees.Dtos;

namespace AltomateHR.Api.Modules.Employees;

// Moving an employee from the active org to another org the same admin runs.
// See EmployeeTransfer for what moves and what doesn't.
public interface IEmployeeTransferService
{
    // Targets + any queued transfer. Null → the user isn't a member here (404).
    Task<EmployeeTransferOptionsDto?> GetOptionsAsync(string userId);

    // Every queued transfer in the active org, for the employee list.
    Task<IReadOnlyList<EmployeeTransferDto>> ListOpenAsync();

    // Schedule a transfer; when the effective date is today it runs now.
    Task<TransferResult> CreateAsync(string userId, CreateEmployeeTransferDto dto);

    // Add the person to another company the admin runs, KEEPING them here —
    // concurrent employment. Same login; a profile there with their personal
    // details (and, optionally, statutory numbers + bank). Ok=false + Error → 400;
    // Error null → 404.
    Task<(bool Ok, DuplicateResultDto? Result, string? Error)> DuplicateAsync(string userId, DuplicateEmployeeDto dto);

    // Cancel a queued transfer. Cancelling one that already ran or was
    // cancelled is a no-op.
    Task<TransferResult> CancelAsync(string userId, string transferId);

    // The daily job's entry point: run one due transfer, in any org. Throws on
    // failure — the caller records the error on the row in a fresh scope.
    Task ExecuteDueAsync(string transferId);

    // A payroll run for `year` was submitted or reverted at `organizationId`.
    // Anyone transferred OUT of it this year (with payroll carried) gets their
    // new company's previous-employment figures recomputed from scratch — the
    // final month at the old company is usually paid AFTER the transfer ran.
    // Best-effort; never throws.
    Task RecomputeCarriedYearToDateAsync(string organizationId, int year);

    // Record a failed attempt so the screen can show why and the job retries.
    Task MarkFailedAsync(string transferId, string error);
}

// Ok=false with Error → 400; Ok=false and Error null → not found (404).
public record TransferResult(
    bool Ok, EmployeeTransferDto? Transfer, string? Error, bool ExecutedImmediately = false);
