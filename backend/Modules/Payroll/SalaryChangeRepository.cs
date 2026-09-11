using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll;

public class SalaryChangeRepository : ISalaryChangeRepository
{
    private readonly AppDbContext _db;

    public SalaryChangeRepository(AppDbContext db) => _db = db;

    public Task<List<SalaryChange>> GetForEmployeeAsync(string employeeProfileId) =>
        _db.SalaryChanges
            .Where(c => c.EmployeeProfileId == employeeProfileId)
            .OrderByDescending(c => c.EffectiveDate)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync();

    public Task<List<SalaryChange>> GetEffectiveInRangeAsync(DateTime from, DateTime to) =>
        _db.SalaryChanges
            .Where(c => c.EffectiveDate >= from && c.EffectiveDate <= to)
            .OrderBy(c => c.EffectiveDate)
            .ToListAsync();

    public async Task<SalaryChange> AddAsync(SalaryChange change)
    {
        _db.SalaryChanges.Add(change);
        await _db.SaveChangesAsync();
        return change;
    }
}
