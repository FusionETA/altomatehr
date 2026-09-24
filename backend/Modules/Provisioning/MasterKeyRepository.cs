using AltomateHR.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Provisioning;

public class MasterKeyRepository : IMasterKeyRepository
{
    private readonly AppDbContext _db;

    public MasterKeyRepository(AppDbContext db) => _db = db;

    public Task<MasterKey?> GetByHashAsync(string tokenHash) =>
        _db.MasterKeys.FirstOrDefaultAsync(k => k.TokenHash == tokenHash);

    public Task<List<MasterKey>> GetAllAsync() =>
        _db.MasterKeys.OrderBy(k => k.Name).ToListAsync();

    public async Task<MasterKey> AddAsync(MasterKey key)
    {
        _db.MasterKeys.Add(key);
        await _db.SaveChangesAsync();
        return key;
    }

    public async Task UpdateAsync(MasterKey key)
    {
        _db.MasterKeys.Update(key);
        await _db.SaveChangesAsync();
    }
}
