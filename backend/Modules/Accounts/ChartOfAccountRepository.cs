using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Accounts.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Accounts;

public class ChartOfAccountRepository : IChartOfAccountRepository
{
    private readonly AppDbContext _db;

    public ChartOfAccountRepository(AppDbContext db) => _db = db;

    public Task<List<ChartOfAccount>> GetAllAsync() =>
        _db.ChartOfAccounts.OrderBy(a => a.Code).ToListAsync();

    public Task<ChartOfAccount?> GetByIdAsync(string id) =>
        _db.ChartOfAccounts.FirstOrDefaultAsync(a => a.Id == id);

    public async Task<ChartOfAccount> AddAsync(ChartOfAccount account)
    {
        _db.ChartOfAccounts.Add(account);
        await _db.SaveChangesAsync();
        return account;
    }

    public async Task UpdateAsync(ChartOfAccount account)
    {
        _db.ChartOfAccounts.Update(account);
        await _db.SaveChangesAsync();
    }
}
