using AltomateHR.Api.Modules.Accounts.Dtos;
using AltomateHR.Api.Modules.Accounts.Entities;

namespace AltomateHR.Api.Modules.Accounts;

public class ChartOfAccountService : IChartOfAccountService
{
    private readonly IChartOfAccountRepository _repo;

    public ChartOfAccountService(IChartOfAccountRepository repo) => _repo = repo;

    public async Task<IEnumerable<ChartOfAccountDto>> GetAllAsync() =>
        (await _repo.GetAllAsync()).Select(ToDto);

    public async Task<ChartOfAccountDto?> GetByIdAsync(string id)
    {
        var account = await _repo.GetByIdAsync(id);
        return account is null ? null : ToDto(account);
    }

    public async Task<ChartOfAccountDto> CreateAsync(SaveChartOfAccountDto dto)
    {
        var account = new ChartOfAccount
        {
            Code = dto.Code,
            Name = dto.Name,
            Type = dto.Type,
            IsSelectable = dto.IsSelectable,
            LimitAmount = dto.LimitAmount,
            AllowMileageClaim = dto.AllowMileageClaim,
            MileageRate = dto.MileageRate,
            CreatedAt = DateTime.UtcNow,
            // OrganizationId is auto-stamped by AppDbContext on save.
        };
        await _repo.AddAsync(account);
        return ToDto(account);
    }

    public async Task<ChartOfAccountDto?> UpdateAsync(string id, SaveChartOfAccountDto dto)
    {
        var account = await _repo.GetByIdAsync(id);
        if (account is null) return null;

        account.Code = dto.Code;
        account.Name = dto.Name;
        account.Type = dto.Type;
        account.IsSelectable = dto.IsSelectable;
        account.LimitAmount = dto.LimitAmount;
        account.AllowMileageClaim = dto.AllowMileageClaim;
        account.MileageRate = dto.MileageRate;
        await _repo.UpdateAsync(account);
        return ToDto(account);
    }

    public async Task<ChartOfAccountDto?> SetArchivedAsync(string id, bool archived)
    {
        var account = await _repo.GetByIdAsync(id);
        if (account is null) return null;

        account.IsArchived = archived;
        await _repo.UpdateAsync(account);
        return ToDto(account);
    }

    private static ChartOfAccountDto ToDto(ChartOfAccount a) => new()
    {
        Id = a.Id,
        Code = a.Code,
        Name = a.Name,
        Type = a.Type,
        IsSelectable = a.IsSelectable,
        LimitAmount = a.LimitAmount,
        AllowMileageClaim = a.AllowMileageClaim,
        MileageRate = a.MileageRate,
        IsArchived = a.IsArchived,
    };
}
