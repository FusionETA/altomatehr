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

public sealed record PayslipEmailResult(bool Ok, string? Error);

public sealed record PayslipEmailFailure(string EmployeeName, string Reason);

public sealed record PayslipEmailBulkResult(
    int Sent, IReadOnlyList<PayslipEmailFailure> Failed, string? Error);
