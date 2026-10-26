using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class UnpaidLeaveSettlementRepository : IUnpaidLeaveSettlementRepository
{
    private readonly AppDbContext _db;

    public UnpaidLeaveSettlementRepository(AppDbContext db) => _db = db;

    public Task<List<UnpaidLeaveSettlement>> GetFinalForYearAsync(int year) =>
        _db.UnpaidLeaveSettlements
            .Where(s => s.Final && s.PeriodYear == year)
            .ToListAsync();

    public async Task ReplaceForRunAsync(string payrollRunId, IReadOnlyList<UnpaidLeaveSettlement> settlements)
    {
        var existing = await _db.UnpaidLeaveSettlements
            .Where(s => s.PayrollRunId == payrollRunId)
            .ToListAsync();
        _db.UnpaidLeaveSettlements.RemoveRange(existing);
        _db.UnpaidLeaveSettlements.AddRange(settlements);
        await _db.SaveChangesAsync();
    }

    public async Task SetFinalForRunsAsync(IReadOnlyCollection<string> payrollRunIds, bool final)
    {
        var rows = await _db.UnpaidLeaveSettlements
            .Where(s => payrollRunIds.Contains(s.PayrollRunId))
            .ToListAsync();
        foreach (var row in rows) row.Final = final;
        await _db.SaveChangesAsync();
    }
}
