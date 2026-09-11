using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// One row per org, so there is no id-based lookup — the tenant filter IS the
// lookup.
public interface IPayrollSettingsRepository
{
    Task<PayrollSettings?> GetAsync();
    Task<PayrollSettings> AddAsync(PayrollSettings settings);
    Task UpdateAsync(PayrollSettings settings);
}
