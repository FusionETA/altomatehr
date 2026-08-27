using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Employees;

public class OrganizationMembershipRepository : IOrganizationMembershipRepository
{
    private readonly AppDbContext _db;

    public OrganizationMembershipRepository(AppDbContext db) => _db = db;

    // Cross-org lookups IgnoreQueryFilters — otherwise the active-org filter would
    // hide every org except the one you're already in, breaking the switcher.
    public Task<List<OrganizationMembership>> GetByUserAsync(string userId) =>
        _db.OrganizationMemberships.IgnoreQueryFilters()
            .Where(m => m.UserId == userId).ToListAsync();

    public Task<OrganizationMembership?> GetAsync(string organizationId, string userId) =>
        _db.OrganizationMemberships.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId);

    public Task<List<OrganizationMembership>> GetForCurrentOrgAsync() =>
        _db.OrganizationMemberships.ToListAsync();

    public Task<OrganizationMembership?> GetForUserInCurrentOrgAsync(string userId) =>
        _db.OrganizationMemberships.FirstOrDefaultAsync(m => m.UserId == userId);

    public Task<List<OrganizationMembership>> GetBySupervisorAsync(string supervisorId) =>
        _db.OrganizationMemberships.Where(m => m.SupervisorId == supervisorId).ToListAsync();

    public Task<int> CountByShiftIdAsync(string shiftId) =>
        _db.OrganizationMemberships.CountAsync(m => m.ShiftId == shiftId);

    public async Task AddAsync(OrganizationMembership membership)
    {
        membership.CreatedAt = membership.CreatedAt == default ? DateTime.UtcNow : membership.CreatedAt;
        membership.UpdatedAt = DateTime.UtcNow;
        _db.OrganizationMemberships.Add(membership);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(OrganizationMembership membership)
    {
        membership.UpdatedAt = DateTime.UtcNow;
        _db.OrganizationMemberships.Update(membership);
        await _db.SaveChangesAsync();
    }
}
