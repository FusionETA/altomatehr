using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Xero.Entities;

namespace AltomateHR.Api.Modules.Xero;

// The Xero apps and each owner's choice of one. Platform-level data, read
// across every company: the connect flow needs it before a company is in
// context (the OAuth callback), and only the superadmin changes it.
public interface IXeroAppRepository
{
    Task<XeroApp?> GetAppAsync(string id);
    Task<XeroApp?> GetSharedAppAsync();
    Task<List<XeroApp>> GetAppsAsync();
    Task AddAppAsync(XeroApp app);
    Task UpdateAppAsync(XeroApp app);
    Task DeleteAppAsync(XeroApp app);

    // Makes `id` the only shared app (or none, with null).
    Task SetSharedAppAsync(string? id);

    Task<XeroOwnerSetting?> GetOwnerSettingAsync(string ownerUserId);
    Task<List<XeroOwnerSetting>> GetOwnerSettingsAsync();
    Task SaveOwnerSettingAsync(XeroOwnerSetting setting);

    // Whose choice a company follows: the superadmin's pick for a co-owned
    // company, else the owner who joined first. Null for a company with no owner.
    Task<string?> GetXeroOwnerOfAsync(string organizationId);

    // ---- superadmin overview, across every company ----
    Task<List<Organization>> GetOrganizationsAsync();
    Task<List<OrganizationMembership>> GetOwnerMembershipsAsync();
    Task<List<User>> GetUsersAsync(IEnumerable<string> ids);
    Task<List<XeroConnection>> GetConnectionsAsync();
    Task<Organization?> GetOrganizationAsync(string id);
    Task UpdateOrganizationAsync(Organization organization);
}
