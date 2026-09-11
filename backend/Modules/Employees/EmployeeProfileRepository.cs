using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Employees;

public class EmployeeProfileRepository : IEmployeeProfileRepository
{
    private readonly AppDbContext _db;

    public EmployeeProfileRepository(AppDbContext db) => _db = db;

    // Tenant filter scopes this to the current org, so UserId is unique within it.
    public Task<EmployeeProfile?> GetByUserAsync(string userId) =>
        _db.EmployeeProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

    public Task<List<EmployeeProfile>> GetUnarchivedPastLeaversAsync(DateTime before, int max) =>
        _db.EmployeeProfiles
            // The sweep has no current org, so the filter would be a no-op
            // anyway — saying so explicitly stops a future reader assuming
            // this is scoped when it is not.
            .IgnoreQueryFilters()
            .Where(p => !p.IsArchived && p.LeaveDate != null && p.LeaveDate < before)
            .OrderBy(p => p.LeaveDate)
            .Take(max)
            .ToListAsync();

    public Task<List<EmployeeProfile>> GetAllForCurrentOrgAsync() =>
        _db.EmployeeProfiles.ToListAsync();

    public async Task<EmployeeProfile> AddAsync(EmployeeProfile profile)
    {
        var now = DateTime.UtcNow;
        profile.CreatedAt = now;
        profile.UpdatedAt = now;
        _db.EmployeeProfiles.Add(profile);   // StampTenant sets OrganizationId = active org
        await _db.SaveChangesAsync();
        return profile;
    }

    public async Task UpdateAsync(EmployeeProfile profile)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }
}
