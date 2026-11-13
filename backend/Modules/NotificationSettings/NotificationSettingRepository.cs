using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.NotificationSettings.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.NotificationSettings;

public class NotificationSettingRepository : INotificationSettingRepository
{
    private readonly AppDbContext _db;

    public NotificationSettingRepository(AppDbContext db) => _db = db;

    public Task<List<NotificationSetting>> GetAllForCurrentOrgAsync() =>
        _db.NotificationSettings.ToListAsync();

    public Task<NotificationSetting?> GetForOrgAsync(string organizationId, NotificationKind kind) =>
        _db.NotificationSettings.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == organizationId && s.Kind == kind);

    public async Task SaveAsync(NotificationSetting setting)
    {
        if (_db.Entry(setting).State == EntityState.Detached) _db.NotificationSettings.Add(setting);
        await _db.SaveChangesAsync();
    }
}
