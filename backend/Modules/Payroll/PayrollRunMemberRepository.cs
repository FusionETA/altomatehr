using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class PayrollRunMemberRepository : IPayrollRunMemberRepository
{
    private readonly AppDbContext _db;

    public PayrollRunMemberRepository(AppDbContext db) => _db = db;

    public Task<List<PayrollRunMember>> GetForRunAsync(string payrollRunId) =>
        _db.PayrollRunMembers
            .Where(m => m.PayrollRunId == payrollRunId)
            .ToListAsync();

    public async Task ReplaceForRunAsync(PayrollRun run, IEnumerable<string> employeeProfileIds)
    {
        var existing = await GetForRunAsync(run.Id);
        if (existing.Count > 0) _db.PayrollRunMembers.RemoveRange(existing);

        var now = DateTime.UtcNow;
        foreach (var profileId in employeeProfileIds.Distinct(StringComparer.Ordinal))
        {
            _db.PayrollRunMembers.Add(new PayrollRunMember
            {
                PayrollRunId = run.Id,
                EmployeeProfileId = profileId,
                PeriodYear = run.PeriodYear,
                PeriodMonth = run.PeriodMonth,
                CreatedAt = now,
            });   // StampTenant sets OrganizationId
        }

        await _db.SaveChangesAsync();
    }
}
