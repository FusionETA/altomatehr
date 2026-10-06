using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Employees;

public class EmploymentPeriodRepository : IEmploymentPeriodRepository
{
    private readonly AppDbContext _db;

    public EmploymentPeriodRepository(AppDbContext db) => _db = db;

    public Task<List<EmploymentPeriod>> GetForUserAsync(string userId) =>
        _db.EmploymentPeriods
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.LeaveDate == null)
            .ThenByDescending(p => p.JoinDate)
            .ThenByDescending(p => p.CreatedAt)
            .ToListAsync();

    public async Task<EmploymentPeriod?> GetOpenAsync(string organizationId, string userId) =>
        // A period staged earlier in this same request isn't in the database yet.
        _db.EmploymentPeriods.Local.FirstOrDefault(p =>
            p.OrganizationId == organizationId && p.UserId == userId && p.LeaveDate == null)
        ?? await _db.EmploymentPeriods.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.UserId == userId && p.LeaveDate == null);

    public async Task<bool> HasPeriodEndingAsync(string organizationId, string userId, DateTime leaveDate) =>
        _db.EmploymentPeriods.Local.Any(p =>
            p.OrganizationId == organizationId && p.UserId == userId && p.LeaveDate == leaveDate.Date)
        || await _db.EmploymentPeriods.IgnoreQueryFilters().AnyAsync(p =>
            p.OrganizationId == organizationId && p.UserId == userId && p.LeaveDate == leaveDate.Date);

    public void Stage(EmploymentPeriod period)
    {
        var now = DateTime.UtcNow;
        period.CreatedAt = now;
        period.UpdatedAt = now;
        _db.EmploymentPeriods.Add(period);
    }
}
