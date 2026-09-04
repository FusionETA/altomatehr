using AltomateHR.Api.Modules.Accounts.Dtos;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Accounts.Entities;

namespace AltomateHR.Api.Modules.Accounts;

public class ChartOfAccountService : IChartOfAccountService
{
    private readonly IChartOfAccountRepository _repo;
    private readonly IXeroService _xero;

    public ChartOfAccountService(IChartOfAccountRepository repo, IXeroService xero)
    {
        _repo = repo;
        _xero = xero;
    }

    public async Task<IEnumerable<ChartOfAccountDto>> GetAllAsync() =>
        (await _repo.GetAllAsync()).Select(ToDto);

    public async Task<ChartOfAccountDto?> GetByIdAsync(string id)
    {
        var account = await _repo.GetByIdAsync(id);
        return account is null ? null : ToDto(account);
    }

    // Once an org is connected to Xero, Xero owns the chart of accounts and this
    // app only mirrors it. A hand-made account would have no Xero counterpart,
    // so any claim coded to it could not carry a valid AccountCode onto a bill —
    // the claim would sync to the wrong account, or to Xero's default.
    //
    // Editing an existing account is still allowed: the spend limit, mileage
    // rate and selectable flag are ours, not Xero's.
    public async Task<ChartOfAccountDto> CreateAsync(SaveChartOfAccountDto dto)
    {
        if (await _xero.IsConnectedAsync())
        {
            throw new ChartOfAccountConflictException(
                "Xero owns the chart of accounts while it's connected. Sync from Xero instead of adding an account here.");
        }

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
        XeroAccountId = a.XeroAccountId,
        XeroStatus = a.XeroStatus,
        XeroSyncedAt = a.XeroSyncedAt,
        IsSelectable = a.IsSelectable,
        LimitAmount = a.LimitAmount,
        AllowMileageClaim = a.AllowMileageClaim,
        MileageRate = a.MileageRate,
        IsArchived = a.IsArchived,
    };
}
