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
    private readonly IAdminAccessResolver _access;

    private readonly IOrganizationStatus? _status;

    public PartnerIdentityService(
        IProvisioningService provisioning,
        IAuthService auth,
        IDirectoryService directory,
        IOrganizationService organizations,
        IApiKeyService apiKeys,
        IAdminAccessResolver access,
        IOrganizationStatus? status = null)
    {
        _status = status;
        _access = access;
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

    public async Task<PartnerIdentity?> VerifyAsync(
        PartnerCaller caller, string email, string password, string? clientIp = null)
    {
        // The password check alone: no session, refresh token or login entry —
        // the integration mints its own session.
        var user = await _auth.VerifyPasswordAsync(email.Trim(), password, LockoutIp(caller, clientIp));
        if (user is null) return null;

        var organizations = await NamedAsync(caller, await AdminMembershipsAsync(caller, user.Id));
        if (organizations.Count == 0) return null;

        var primary = organizations[0];
        return new PartnerIdentity(
            user.Id, user.Name, user.Email, primary.Role,
            primary.Id, primary.Name, organizations);
    }

    // Whose wrong passwords these are, for the IP rule (LoginGuard):
    //   • a master key (ABPay) passing the person's IP: that IP — a guesser
    //     at ABPay's login is blocked as one at AltomateHR's would be;
    //   • a master key passing none (a companion not yet sending it): no IP
    //     rule, or every one of its users would share one block;
    //   • a company API key: its own bucket — anyone holding a key who sprays
    //     passwords is stopped at 10, without blocking any real IP.
    private static string? LockoutIp(PartnerCaller caller, string? clientIp)
    {
        if (!caller.IsMaster) return $"apikey:{caller.OrganizationId}";
        var ip = clientIp?.Trim();
        return !string.IsNullOrEmpty(ip) && System.Net.IPAddress.TryParse(ip, out _) ? ip : null;
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
            : await NamedAsync(caller, await AdminMembershipsAsync(caller, id));
    }

    public async Task<PartnerAccess?> AccessAsync(PartnerCaller caller, string userId, string organizationId)
    {
        // A company key sees its own company in full; letting it ask what
        // ANOTHER person may reach would turn it into a directory of who is
        // restricted. Only the platform's own master key may ask.
        if (!caller.IsMaster) return null;

        var snapshot = await _access.ResolveAsync(organizationId, userId);
        if (snapshot is null) return null;

        return new PartnerAccess(
            userId,
            organizationId,
            snapshot.Role,
            snapshot.AllEmployees,
            snapshot.CanRunPayroll,
            snapshot.CanChangeSettings,
            snapshot.Modules.ToDictionary(m => m.Key, m => m.Value.ToString(), StringComparer.OrdinalIgnoreCase),
            snapshot.Employees?.Select(e => new PartnerAccessEmployee(e.UserId, e.EmployeeProfileId)).ToList());
    }

    public async Task<string?> KeyNameAsync(string apiKeyId) =>
        (await _apiKeys.GetAllAsync()).FirstOrDefault(k => k.Id == apiKeyId)?.Name;

    // Admin/Owner memberships the caller may see: an org key only its own
    // company — never the person's other employers — a master key all of them.
    // A deactivated company is never one of them.
    private async Task<List<OrganizationMembership>> AdminMembershipsAsync(PartnerCaller caller, string userId)
    {
        var deactivated = _status is null ? new HashSet<string>() : await _status.DeactivatedIdsAsync();
        return (await _directory.GetMembershipsByUserAsync(userId))
            .Where(m => OrgRoles.IsAdministrative(m.Role))
            .Where(m => caller.OrganizationId is null || m.OrganizationId == caller.OrganizationId)
            .Where(m => !deactivated.Contains(m.OrganizationId))
            .ToList();
    }

    // Each company with this person's role in it and — for a MASTER caller
    // only — their Manage access there, so a companion knows up front where it
    // must narrow itself. An org key is told nothing about a person's limits
    // (see PartnerOrganization). The flags are two membership columns and the
    // org's package: no employee resolution, which is what /auth/access is for.
    private async Task<List<PartnerOrganization>> NamedAsync(
        PartnerCaller caller, IEnumerable<OrganizationMembership> memberships)
    {
        var named = new List<PartnerOrganization>();
        foreach (var m in memberships)
        {
            var org = await _organizations.GetByIdAsync(m.OrganizationId);
            var flags = caller.IsMaster ? await _access.FlagsAsync(m.OrganizationId, m) : null;
            named.Add(new PartnerOrganization(
                m.OrganizationId, org?.Name ?? m.OrganizationId, m.Role,
                flags?.AllEmployees, flags?.CanRunPayroll));
        }
        return named;
    }
}
