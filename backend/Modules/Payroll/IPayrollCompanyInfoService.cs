using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public interface IPayrollCompanyInfoService
{
    // Never 404s — an unconfigured org gets an empty profile flagged with
    // IsConfigured = false.
    Task<PayrollCompanyInfoDto> GetAsync();

    Task<PayrollCompanyInfo?> GetEntityAsync();

    Task<PayrollCompanyInfoDto> SaveAsync(SavePayrollCompanyInfoDto dto);
}
