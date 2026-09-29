using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Provisioning;

namespace AltomateHR.Api.Modules.ApiKeys;

// Tenancy: under an org key the global query filter already scopes membership
// reads to that one company. Under a master key there is no org, so the filter
// is off — deliberately, and only for this: all a master caller learns is
// whether a supplied password is right for an admin/owner and which companies
// that person administers, as the previous system's companion endpoints did.
public class PartnerIdentityService : IPartnerIdentityService
{
    private readonly IProvisioningService _provisioning;
    private readonly IAuthService _auth;
    private readonly IDirectoryService _directory;
    private readonly IOrganizationService _organizations;
    private readonly IApiKeyService _apiKeys;

    public PartnerIdentityService(
        IProvisioningService provisioning,
        IAuthService auth,
        IDirectoryService directory,
        IOrganizationService organizations,
        IApiKeyService apiKeys)
    {
        _provisioning = provisioning;
        _auth = auth;
        _directory = directory;
        _organizations = organizations;
        _apiKeys = apiKeys;
    }

    public async Task<PartnerCaller?> ResolveCallerAsync(string? bearerToken, string? keyOrganizationId)
    {
        if (bearerToken?.StartsWith(MasterTokenGenerator.Prefix, StringComparison.Ordinal) == true)
        {
            return await _provisioning.AuthenticateAsync(bearerToken) is null
                ? null
                : new PartnerCaller(IsMaster: true, OrganizationId: null);
        }

        return string.IsNullOrEmpty(keyOrganizationId)
            ? null
            : new PartnerCaller(IsMaster: false, keyOrganizationId);
    }

    public async Task<PartnerIdentity?> VerifyAsync(PartnerCaller caller, string email, string password)
    {
        // The password check alone: no session, refresh token or login entry —
        // the integration mints its own session.
        var user = await _auth.VerifyPasswordAsync(email.Trim(), password);
        if (user is null) return null;

        var organizations = await NamedAsync(await AdminMembershipsAsync(caller, user.Id));
        if (organizations.Count == 0) return null;

        var primary = organizations[0];
        return new PartnerIdentity(
            user.Id, user.Name, user.Email, primary.Role,
            primary.Id, primary.Name, organizations);
    }

    public async Task<IReadOnlyList<PartnerOrganization>> AdminOrganizationsAsync(
        PartnerCaller caller, string? userId, string? email)
    {
        var id = userId?.Trim();
        if (string.IsNullOrEmpty(id) && !string.IsNullOrWhiteSpace(email))
        {
            id = (await _directory.GetUsersAsync())
                .FirstOrDefault(u => string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;
        }

        return string.IsNullOrEmpty(id)
            ? []
            : await NamedAsync(await AdminMembershipsAsync(caller, id));
    }

    public async Task<string?> KeyNameAsync(string apiKeyId) =>
        (await _apiKeys.GetAllAsync()).FirstOrDefault(k => k.Id == apiKeyId)?.Name;

    // Admin/Owner memberships the caller may see: an org key only its own
    // company — never the person's other employers — a master key all of them.
    private async Task<List<OrganizationMembership>> AdminMembershipsAsync(PartnerCaller caller, string userId) =>
        (await _directory.GetMembershipsByUserAsync(userId))
            .Where(m => OrgRoles.IsAdministrative(m.Role))
            .Where(m => caller.OrganizationId is null || m.OrganizationId == caller.OrganizationId)
            .ToList();

    private async Task<List<PartnerOrganization>> NamedAsync(IEnumerable<OrganizationMembership> memberships)
    {
        var named = new List<PartnerOrganization>();
        foreach (var m in memberships)
        {
            var org = await _organizations.GetByIdAsync(m.OrganizationId);
            named.Add(new PartnerOrganization(m.OrganizationId, org?.Name ?? m.OrganizationId, m.Role));
        }
        return named;
    }
}
