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
            // Not while their company is deactivated; caught up on reactivation.
            .Where(p => !_db.Organizations.Any(o => o.Id == p.OrganizationId && o.DeactivatedAt != null))
            .OrderBy(p => p.LeaveDate)
            .Take(max)
            .ToListAsync();

    public Task<EmployeeProfile?> GetByUserInOrgAsync(string organizationId, string userId) =>
        _db.EmployeeProfiles.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.OrganizationId == organizationId && p.UserId == userId);

    public Task<List<EmployeeProfile>> GetByUserAcrossOrgsAsync(string userId) =>
        _db.EmployeeProfiles.IgnoreQueryFilters().Where(p => p.UserId == userId).ToListAsync();

    public Task<List<EmployeeProfile>> GetAllForCurrentOrgAsync() =>
        _db.EmployeeProfiles.ToListAsync();

    public Task<List<EmployeeProfile>> GetAllForOrgAsync(string organizationId) =>
        _db.EmployeeProfiles.IgnoreQueryFilters()
            .Where(p => p.OrganizationId == organizationId).ToListAsync();

    public Task<EmployeeProfile?> GetByIdAsync(string id) =>
        _db.EmployeeProfiles.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<string?> SetPayslipPasswordIfNoneAsync(string profileId, string encrypted)
    {
        if (_db.Database.IsRelational())
        {
            // One conditional UPDATE: whoever is second changes nothing. The
            // tenant filter applies, so another org's profile is never touched.
            await _db.EmployeeProfiles
                .Where(p => p.Id == profileId && p.PayslipPasswordEncrypted == null)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.PayslipPasswordEncrypted, encrypted));
        }
        else
        {
            // The in-memory test provider has no ExecuteUpdate.
            var profile = await _db.EmployeeProfiles.FirstOrDefaultAsync(p => p.Id == profileId);
            if (profile is not null && profile.PayslipPasswordEncrypted is null)
            {
                profile.PayslipPasswordEncrypted = encrypted;
                await _db.SaveChangesAsync();
            }
        }

        return await _db.EmployeeProfiles.AsNoTracking()
            .Where(p => p.Id == profileId)
            .Select(p => p.PayslipPasswordEncrypted)
            .FirstOrDefaultAsync();
    }

    public async Task SetPayslipPasswordAsync(string profileId, string encrypted)
    {
        var profile = await _db.EmployeeProfiles.FirstOrDefaultAsync(p => p.Id == profileId);
        if (profile is null) return;
        profile.PayslipPasswordEncrypted = encrypted;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

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
