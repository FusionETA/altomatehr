using AltomateHR.Api.Modules.Accounts.Dtos;

namespace AltomateHR.Api.Modules.Accounts;

public interface IChartOfAccountService
{
    // The claims chart — expense and bank accounts. Liability accounts exist
    // only for the payroll journal, so they are left out unless asked for.
    Task<IEnumerable<ChartOfAccountDto>> GetAllAsync(bool includeLiabilities = false);
    Task<ChartOfAccountDto?> GetByIdAsync(string id);
    Task<ChartOfAccountDto> CreateAsync(SaveChartOfAccountDto dto);
    Task<ChartOfAccountDto?> UpdateAsync(string id, SaveChartOfAccountDto dto);
    Task<ChartOfAccountDto?> SetArchivedAsync(string id, bool archived);
}
