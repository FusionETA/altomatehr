using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.NotificationSettings.Dtos;
using AltomateHR.Api.Modules.NotificationSettings.Entities;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.NotificationSettings;

public class NotificationSettingService : INotificationSettingService
{
    private readonly INotificationSettingRepository _repo;
    private readonly IDirectoryService _directory;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;

    public NotificationSettingService(
        INotificationSettingRepository repo,
        IDirectoryService directory,
        ICurrentUser currentUser,
        IAuditService audit)
    {
        _repo = repo;
        _directory = directory;
        _currentUser = currentUser;
        _audit = audit;
    }

    // What a company gets before anyone saves: on, to the owner, staff told
    // in the app only. Emailing every staff member is a choice to make.
    private static NotificationSetting Defaults(NotificationKind kind) =>
        new() { Kind = kind, Enabled = true, EmailStaff = false };

    private static (string Title, string Description) Describe(NotificationKind kind) => kind switch
    {
        NotificationKind.DOCUMENT_EXPIRY => (
            "Work permit & passport expiry",
            "Foreign staff only · 30 days before and on the expiry day · sent at 08:00"),
        _ => (kind.ToString(), ""),
    };

    public async Task<NotificationSettingsDto> GetAsync()
    {
        var members = await _directory.GetMembershipsForCurrentOrgAsync();
        var admins = members.Where(m => OrgRoles.IsAdministrative(m.Role)).ToList();
        var users = await NamesAsync(admins.Select(a => a.UserId));
        var saved = (await _repo.GetAllForCurrentOrgAsync()).ToDictionary(s => s.Kind);

        var dto = new NotificationSettingsDto
        {
            // The owner first, then admins by name.
            Admins = [.. admins
                .Select(a => new NotificationAdminDto
                {
                    UserId = a.UserId,
                    Name = users.GetValueOrDefault(a.UserId).Name ?? "",
                    Email = users.GetValueOrDefault(a.UserId).Email ?? "",
                    Role = a.Role,
                    IsLimited = IsLimited(a),
                })
                .OrderBy(a => IsOwner(a.Role) ? 0 : 1)
                .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)],
        };

        var adminIds = admins.Select(a => a.UserId).ToHashSet(StringComparer.Ordinal);
        foreach (var kind in Enum.GetValues<NotificationKind>())
        {
            var setting = saved.GetValueOrDefault(kind) ?? Defaults(kind);
            var editor = setting.UpdatedByUserId is { } by ? await _directory.GetUserAsync(by) : null;
            dto.Notifications.Add(ToDto(setting, adminIds, editor?.Name));
        }
        return dto;
    }

    public async Task<NotificationSettingDto> UpdateAsync(NotificationKind kind, UpdateNotificationSettingDto dto)
    {
        var members = await _directory.GetMembershipsForCurrentOrgAsync();
        var adminIds = members.Where(m => OrgRoles.IsAdministrative(m.Role))
            .Select(m => m.UserId).ToHashSet(StringComparer.Ordinal);

        var picked = dto.AdminUserIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (picked.Any(id => !adminIds.Contains(id)))
            throw new ArgumentException("Only the owner or an admin of this company can be chosen.");

        var setting = (await _repo.GetAllForCurrentOrgAsync()).FirstOrDefault(s => s.Kind == kind)
                      ?? new NotificationSetting { Kind = kind };
        setting.Enabled = dto.Enabled;
        setting.AdminUserIds = picked.Count == 0 ? null : OrgModules.Join(picked);
        setting.EmailStaff = dto.EmailStaff;
        setting.UpdatedAt = DateTime.UtcNow;
        setting.UpdatedByUserId = _currentUser.UserId;
        await _repo.SaveAsync(setting);

        var names = await NamesAsync(picked);
        var title = Describe(kind).Title;
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.SettingsNotificationsUpdate,
            !dto.Enabled
                ? $"{title} · off"
                : $"{title} · to {(picked.Count == 0 ? "the owner" : string.Join(", ", picked.Select(id => names.GetValueOrDefault(id).Name ?? id)))}"
                  + (dto.EmailStaff ? " · staff emailed" : ""),
            TargetType: "NotificationSetting",
            TargetId: setting.Id,
            Metadata: new { Kind = kind.ToString(), dto.Enabled, AdminUserIds = picked, dto.EmailStaff }));

        var editor = _currentUser.UserId is { } me ? await _directory.GetUserAsync(me) : null;
        return ToDto(setting, adminIds, editor?.Name);
    }

    public async Task<EffectiveNotificationSetting> ForOrgAsync(string organizationId, NotificationKind kind)
    {
        var setting = await _repo.GetForOrgAsync(organizationId, kind) ?? Defaults(kind);
        if (!setting.Enabled) return new EffectiveNotificationSetting(false, false, [], []);

        var admins = (await _directory.GetMembershipsForOrgAsync(organizationId))
            .Where(m => m.OrganizationId == organizationId && OrgRoles.IsAdministrative(m.Role))
            .ToList();

        var picked = OrgModules.Split(setting.AdminUserIds).ToHashSet(StringComparer.Ordinal);
        var owners = admins.Where(a => IsOwner(a.Role)).ToList();
        var chosen = admins.Where(a => picked.Contains(a.UserId)).ToList();
        // Nobody ticked, or everyone ticked has gone: the owner, so a
        // reminder is never sent to nobody.
        if (chosen.Count == 0) chosen = owners;

        return new EffectiveNotificationSetting(
            true,
            setting.EmailStaff,
            [.. chosen.Select(a => new NotificationRecipient(
                a.UserId, IsLimited(a) ? OrgModules.Split(a.PolicyScope) : null))],
            [.. owners.Select(o => o.UserId)]);
    }

    private static NotificationSettingDto ToDto(
        NotificationSetting s, IReadOnlySet<string> adminIds, string? updatedByName)
    {
        var (title, description) = Describe(s.Kind);
        return new NotificationSettingDto
        {
            Kind = s.Kind,
            Title = title,
            Description = description,
            Enabled = s.Enabled,
            // Someone ticked who has since stopped being an admin isn't shown.
            AdminUserIds = [.. OrgModules.Split(s.AdminUserIds).Where(adminIds.Contains)],
            EmailStaff = s.EmailStaff,
            UpdatedAt = s.UpdatedByUserId is null ? null : s.UpdatedAt,
            UpdatedByName = updatedByName,
        };
    }

    private static bool IsOwner(string role) => string.Equals(role, OrgRoles.Owner, StringComparison.OrdinalIgnoreCase);

    // Only an Admin is ever limited — an Owner sees everyone.
    private static bool IsLimited(OrganizationMembership m) =>
        string.Equals(m.Role, OrgRoles.Admin, StringComparison.OrdinalIgnoreCase) && m.PolicyScope is not null;

    private async Task<Dictionary<string, (string? Name, string? Email)>> NamesAsync(IEnumerable<string> userIds)
    {
        var result = new Dictionary<string, (string? Name, string? Email)>(StringComparer.Ordinal);
        foreach (var id in userIds.Distinct(StringComparer.Ordinal))
        {
            var user = await _directory.GetUserAsync(id);
            if (user is not null) result[id] = (user.Name, user.Email);
        }
        return result;
    }
}
