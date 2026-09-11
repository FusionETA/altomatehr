using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

public interface IPayrollCompanyInfoRepository
{
    Task<PayrollCompanyInfo?> GetAsync();
    Task<PayrollCompanyInfo> AddAsync(PayrollCompanyInfo info);
    Task UpdateAsync(PayrollCompanyInfo info);
}
