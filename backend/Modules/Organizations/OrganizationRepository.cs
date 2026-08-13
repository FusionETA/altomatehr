using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Organizations.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Organizations;

public class OrganizationRepository : IOrganizationRepository
{
    private readonly AppDbContext _db;

    public OrganizationRepository(AppDbContext db) => _db = db;

    public Task<Organization?> GetByIdAsync(string id) =>
        _db.Organizations.FirstOrDefaultAsync(o => o.Id == id);

    public Task<Organization?> GetFirstAsync() =>
        _db.Organizations.OrderBy(o => o.CreatedAt).FirstOrDefaultAsync();

    public async Task AddAsync(Organization organization)
    {
        _db.Organizations.Add(organization);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAsync(Organization organization)
    {
        _db.Organizations.Update(organization);
        await _db.SaveChangesAsync();
    }

    public Task<bool> AnyAsync() => _db.Organizations.AnyAsync();
}
