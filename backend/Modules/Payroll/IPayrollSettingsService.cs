using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public interface IPayrollSettingsService
{
    // Never 404s — an org with no row yet gets the statutory defaults, flagged
    // with IsConfigured = false.
    Task<PayrollSettingsDto> GetAsync();

    // The entity form, for payroll code that needs the rules rather than a
    // response shape. Falls back to a transient default instance.
    Task<PayrollSettings> GetEffectiveAsync();

    Task<PayrollSettingsDto> SaveAsync(SavePayrollSettingsDto dto);
}
