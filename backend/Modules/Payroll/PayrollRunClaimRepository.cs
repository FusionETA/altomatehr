using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollRunClaimRepository : IPayrollRunClaimRepository
{
    private readonly AppDbContext _db;

    public PayrollRunClaimRepository(AppDbContext db) => _db = db;

    public Task<List<PayrollRunClaim>> GetForRunAsync(string payrollRunId) =>
        _db.PayrollRunClaims
            .Where(c => c.PayrollRunId == payrollRunId)
            .ToListAsync();

    public Task<List<PayrollRunClaim>> GetAllAsync() =>
        _db.PayrollRunClaims.ToListAsync();

    public Task<PayrollRunClaim?> GetByClaimIdAsync(string claimId) =>
        _db.PayrollRunClaims.FirstOrDefaultAsync(c => c.ClaimId == claimId);

    public async Task<PayrollRunClaim> AddAsync(PayrollRunClaim attachment)
    {
        var now = DateTime.UtcNow;
        attachment.CreatedAt = now;
        attachment.UpdatedAt = now;
        _db.PayrollRunClaims.Add(attachment);   // StampTenant sets OrganizationId
        await _db.SaveChangesAsync();
        return attachment;
    }

    public async Task DeleteForRunAsync(string payrollRunId)
    {
        var rows = await GetForRunAsync(payrollRunId);
        if (rows.Count == 0) return;

        _db.PayrollRunClaims.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> DeleteByClaimIdAsync(string claimId)
    {
        var existing = await GetByClaimIdAsync(claimId);
        if (existing is null) return false;

        _db.PayrollRunClaims.Remove(existing);
        await _db.SaveChangesAsync();
        return true;
    }
}
