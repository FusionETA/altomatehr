using AltomateHR.Api.Modules.Accounts.Entities;

namespace AltomateHR.Api.Modules.Accounts;

public interface IChartOfAccountRepository
{
    Task<List<ChartOfAccount>> GetAllAsync();
    Task<ChartOfAccount?> GetByIdAsync(string id);
    Task<ChartOfAccount> AddAsync(ChartOfAccount account);
    Task UpdateAsync(ChartOfAccount account);
}
