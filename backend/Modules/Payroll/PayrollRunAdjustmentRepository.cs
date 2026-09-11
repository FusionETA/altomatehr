using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollRunAdjustmentRepository : IPayrollRunAdjustmentRepository
{
    private readonly AppDbContext _db;

    public PayrollRunAdjustmentRepository(AppDbContext db) => _db = db;

    public Task<List<PayrollRunAdjustment>> GetForRunAsync(string payrollRunId) =>
        _db.PayrollRunAdjustments
            .Where(a => a.PayrollRunId == payrollRunId)
            .ToListAsync();

    public Task<PayrollRunAdjustment?> GetAsync(string payrollRunId, string employeeProfileId) =>
        _db.PayrollRunAdjustments.FirstOrDefaultAsync(
            a => a.PayrollRunId == payrollRunId && a.EmployeeProfileId == employeeProfileId);

    public async Task<PayrollRunAdjustment> UpsertAsync(PayrollRunAdjustment adjustment)
    {
        var now = DateTime.UtcNow;

        var existing = await GetAsync(adjustment.PayrollRunId, adjustment.EmployeeProfileId);
        if (existing is null)
        {
            adjustment.CreatedAt = now;
            adjustment.UpdatedAt = now;
            _db.PayrollRunAdjustments.Add(adjustment);   // StampTenant sets OrganizationId
            await _db.SaveChangesAsync();
            return adjustment;
        }

        existing.OtNormalHours = adjustment.OtNormalHours;
        existing.OtRestHours = adjustment.OtRestHours;
        existing.OtPublicHours = adjustment.OtPublicHours;
        existing.ManualLineItemsJson = adjustment.ManualLineItemsJson;
        existing.FixedAllowanceOverridesJson = adjustment.FixedAllowanceOverridesJson;
        existing.WorkedHours = adjustment.WorkedHours;
        existing.ExpectedHours = adjustment.ExpectedHours;
        existing.Notes = adjustment.Notes;
        existing.UpdatedAt = now;

        await _db.SaveChangesAsync();
        return existing;
    }

    public async Task DeleteForRunAsync(string payrollRunId)
    {
        var rows = await GetForRunAsync(payrollRunId);
        if (rows.Count == 0) return;

        _db.PayrollRunAdjustments.RemoveRange(rows);
        await _db.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string payrollRunId, string employeeProfileId)
    {
        var existing = await GetAsync(payrollRunId, employeeProfileId);
        if (existing is null) return false;

        _db.PayrollRunAdjustments.Remove(existing);
        await _db.SaveChangesAsync();
        return true;
    }
}
