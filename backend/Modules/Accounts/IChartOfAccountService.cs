using AltomateHR.Api.Modules.Accounts.Dtos;

namespace AltomateHR.Api.Modules.Accounts;

public interface IChartOfAccountService
{
    Task<IEnumerable<ChartOfAccountDto>> GetAllAsync();
    Task<ChartOfAccountDto?> GetByIdAsync(string id);
    Task<ChartOfAccountDto> CreateAsync(SaveChartOfAccountDto dto);
    Task<ChartOfAccountDto?> UpdateAsync(string id, SaveChartOfAccountDto dto);
    Task<ChartOfAccountDto?> SetArchivedAsync(string id, bool archived);
}
