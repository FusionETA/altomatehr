using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Claims.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Claims;

// The ONLY place that touches the database for claims (via EF Core).
public class ClaimsRepository : IClaimsRepository
{
    private readonly AppDbContext _db;

    public ClaimsRepository(AppDbContext db) => _db = db;

    public Task<List<Claim>> GetAllAsync() => _db.Claims.ToListAsync();

    public Task<List<Claim>> GetByEmployeeIdAsync(string employeeId) =>
        _db.Claims.Where(c => c.EmployeeId == employeeId).ToListAsync();

    public Task<Claim?> GetByIdAsync(string id) =>
        _db.Claims.FirstOrDefaultAsync(c => c.Id == id);

    public Task<Claim?> GetByReceiptUrlAsync(string receiptUrl) =>
        _db.Claims.FirstOrDefaultAsync(c => c.ReceiptUrl == receiptUrl);

    public async Task<Claim> AddAsync(Claim claim)
    {
        _db.Claims.Add(claim);
        await _db.SaveChangesAsync();
        return claim;
    }

    public async Task UpdateAsync(Claim claim)
    {
        _db.Claims.Update(claim);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var claim = await _db.Claims.FindAsync(id);
        if (claim is null) return false;
        _db.Claims.Remove(claim);
        await _db.SaveChangesAsync();
        return true;
    }
}
