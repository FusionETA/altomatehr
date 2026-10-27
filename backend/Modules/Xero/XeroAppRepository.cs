using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Xero.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Xero;

public class XeroAppRepository : IXeroAppRepository
{
    private readonly AppDbContext _db;

    public XeroAppRepository(AppDbContext db) => _db = db;

    public Task<XeroApp?> GetAppAsync(string id) =>
        _db.XeroApps.FirstOrDefaultAsync(a => a.Id == id);

    public Task<XeroApp?> GetSharedAppAsync() =>
        _db.XeroApps.FirstOrDefaultAsync(a => a.IsShared);

    public Task<List<XeroApp>> GetAppsAsync() =>
        _db.XeroApps.OrderBy(a => a.Name).ToListAsync();

    public async Task AddAppAsync(XeroApp app)
    {
        _db.XeroApps.Add(app);
        await _db.SaveChangesAsync();
    }

    public async Task UpdateAppAsync(XeroApp app)
    {
        _db.XeroApps.Update(app);
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAppAsync(XeroApp app)
    {
        _db.XeroApps.Remove(app);
        await _db.SaveChangesAsync();
    }

    public async Task SetSharedAppAsync(string? id)
    {
        foreach (var app in await _db.XeroApps.ToListAsync())
            app.IsShared = app.Id == id;
        await _db.SaveChangesAsync();
    }

    public Task<XeroOwnerSetting?> GetOwnerSettingAsync(string ownerUserId) =>
        _db.XeroOwnerSettings.FirstOrDefaultAsync(s => s.OwnerUserId == ownerUserId);

    public Task<List<XeroOwnerSetting>> GetOwnerSettingsAsync() =>
        _db.XeroOwnerSettings.ToListAsync();

    public async Task SaveOwnerSettingAsync(XeroOwnerSetting setting)
    {
        if (_db.Entry(setting).State == EntityState.Detached
            && !await _db.XeroOwnerSettings.AnyAsync(s => s.Id == setting.Id))
        {
            _db.XeroOwnerSettings.Add(setting);
        }
        await _db.SaveChangesAsync();
    }

    public async Task<string?> GetXeroOwnerOfAsync(string organizationId)
    {
        // Unfiltered: the OAuth callback asks before any company is in context.
        var owners = await _db.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(m => m.OrganizationId == organizationId && m.Role == OrgRoles.Owner)
            .OrderBy(m => m.CreatedAt)
            .Select(m => m.UserId)
            .ToListAsync();

        var picked = await _db.Organizations
            .Where(o => o.Id == organizationId)
            .Select(o => o.XeroOwnerUserId)
            .FirstOrDefaultAsync();

        // The pick only counts while that person is still an owner here.
        return picked is not null && owners.Contains(picked) ? picked : owners.FirstOrDefault();
    }

    public Task<List<Organization>> GetOrganizationsAsync() =>
        _db.Organizations.OrderBy(o => o.Name).ToListAsync();

    public Task<List<OrganizationMembership>> GetOwnerMembershipsAsync() =>
        _db.OrganizationMemberships
            .IgnoreQueryFilters()
            .Where(m => m.Role == OrgRoles.Owner)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

    public Task<List<User>> GetUsersAsync(IEnumerable<string> ids)
    {
        var list = ids.Distinct().ToList();
        return _db.Users.Where(u => list.Contains(u.Id)).ToListAsync();
    }

    public Task<List<XeroConnection>> GetConnectionsAsync() =>
        _db.XeroConnections.IgnoreQueryFilters().ToListAsync();

    public Task<Organization?> GetOrganizationAsync(string id) =>
        _db.Organizations.FirstOrDefaultAsync(o => o.Id == id);

    public async Task UpdateOrganizationAsync(Organization organization)
    {
        _db.Organizations.Update(organization);
        await _db.SaveChangesAsync();
    }
}
