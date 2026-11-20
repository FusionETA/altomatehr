using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public sealed record PayslipPasswordSaveResult(PayrollSettingsDto? Settings, string? Error);

public interface IPayrollSettingsService
{
    // Never 404s — an org with no row yet gets the statutory defaults, flagged
    // with IsConfigured = false.
    Task<PayrollSettingsDto> GetAsync();

    // The entity form, for payroll code that needs the rules rather than a
    // response shape. Falls back to a transient default instance.
    Task<PayrollSettings> GetEffectiveAsync();

    Task<PayrollSettingsDto> SaveAsync(SavePayrollSettingsDto dto);

    // When payroll here started, for the year-end forms (PayrollSettings.
    // PayrollStartYear/Month). Year and month together, or both null to clear.
    // Default keeps hand-written test doubles compiling.
    Task SetPayrollStartAsync(int? year, int? month) => Task.CompletedTask;

    // Payslip passwords (PayrollSettings.LockPayslips / PayslipPasswordRule).
    // Not a payroll input, so drafts aren't marked stale. Default keeps
    // hand-written test doubles compiling.
    // Refused (Error set, nothing saved) when the COMPANY rule is switched on
    // with no password saved or given, or the password given is too short.
    Task<PayslipPasswordSaveResult> SavePayslipPasswordAsync(SavePayslipPasswordDto dto) =>
        throw new NotSupportedException();

    // The COMPANY rule's password in clear, for locking files. Null when none
    // is saved. Not for responses — RevealPayslipPasswordAsync is, audited.
    Task<string?> GetPayslipCompanyPasswordAsync() => Task.FromResult<string?>(null);

    // The same, for an admin who clicked Reveal. Audited.
    Task<string?> RevealPayslipPasswordAsync() => Task.FromResult<string?>(null);
}
