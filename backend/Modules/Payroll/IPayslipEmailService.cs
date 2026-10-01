namespace AltomateHR.Api.Modules.Payroll;

// Manual, admin-triggered payslip email — per employee or for a whole run at
// once. Never automatic, so there's no accidental blast. Ported from the
// monolith's payslip-email.service.ts, minus PDF encryption (deliberately out
// of scope for now).
public interface IPayslipEmailService
{
    // Keyed the same way as the existing single-payslip download
    // (PayrollRunsController's documents/payslip/{employeeProfileId}), so
    // the frontend can reuse the same id it already has per row.
    Task<PayslipEmailResult> EmailPayslipAsync(string runId, string employeeProfileId);

    Task<PayslipEmailBulkResult> EmailPayslipsForRunAsync(string runId);
}

// NotFound: the run (or the employee's payslip on it) doesn't exist → 404,
// as opposed to a run that exists but can't be emailed yet → 409.
public sealed record PayslipEmailResult(bool Ok, string? Error, bool NotFound = false);

public sealed record PayslipEmailFailure(string EmployeeName, string Reason);

public sealed record PayslipEmailBulkResult(
    int Sent, IReadOnlyList<PayslipEmailFailure> Failed, string? Error, bool NotFound = false);
