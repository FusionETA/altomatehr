using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Dtos;
using AltomateHR.Api.Modules.Support.Dtos;

namespace AltomateHR.Api.Modules.Support;

public class SupportService : ISupportService
{
    private readonly IOrganizationRepository _organizations;
    private readonly IOrganizationService _organizationService;
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly IAuditService _audit;
    private readonly ICurrentUser _currentUser;
    private readonly IOrganizationStatus? _status;
    private readonly TeamChat.ITeamChatAlerts? _teamChat;
    private readonly ISuperadminRegistry? _superadmins;

    public SupportService(
        IOrganizationRepository organizations,
        IOrganizationService organizationService,
        IOrganizationMembershipRepository memberships,
        IUserRepository users,
        IAuditService audit,
        ICurrentUser currentUser,
        IOrganizationStatus? status = null,
        TeamChat.ITeamChatAlerts? teamChat = null,
        ISuperadminRegistry? superadmins = null)
    {
        _superadmins = superadmins;
        _status = status;
        _teamChat = teamChat;
        _organizations = organizations;
        _organizationService = organizationService;
        _memberships = memberships;
        _users = users;
        _audit = audit;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<SupportOrganizationDto>> ListOrganizationsAsync(string? search)
    {
        var orgs = await _organizations.GetAllAsync();
        var byOrg = (await _memberships.GetAcrossAllOrgsAsync())
            .GroupBy(m => m.OrganizationId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var users = (await _users.GetAllAsync()).ToDictionary(u => u.Id, StringComparer.Ordinal);

        var rows = orgs.Select(org =>
        {
            var members = byOrg.GetValueOrDefault(org.Id) ?? [];
            var owner = members
                .Where(m => string.Equals(m.Role, OrgRoles.Owner, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.CreatedAt)
                .Select(m => users.GetValueOrDefault(m.UserId))
                .FirstOrDefault(u => u is not null);

            return new SupportOrganizationDto
            {
                Id = org.Id,
                Name = org.Name,
                Plan = org.Plan.ToString(),
                Tier = org.Tier?.ToString(),
                Addons = [.. org.Addons.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
                OwnerName = owner?.Name,
                OwnerEmail = owner?.Email,
                EmployeeCount = members.Count(m => OrgRoles.IsOnPayroll(m.Role)),
                CreatedAt = org.CreatedAt,
                DeactivatedAt = org.DeactivatedAt,
                DeactivatedReason = org.DeactivatedReason,
                DeactivatedBy = org.DeactivatedBy,
            };
        });

        // By company name or owner email, as the previous system's picker.
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            rows = rows.Where(r =>
                r.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || (r.OwnerEmail?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        return [.. rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<CreateSupportCompanyResultDto> CreateCompanyAsync(CreateSupportCompanyDto dto)
    {
        var email = dto.OwnerEmail.Trim().ToLowerInvariant();

        // An existing account is made Owner of the new company and keeps its
        // own password — support never overwrites someone's sign-in.
        var owner = await _users.GetByEmailAsync(email);
        var created = false;
        if (owner is null)
        {
            if (string.IsNullOrWhiteSpace(dto.Password) || dto.Password.Length < 8)
            {
                throw new ArgumentException("Set a password of at least 8 characters for the new owner account.");
            }

            owner = new User
            {
                Email = email,
                Name = dto.OwnerName.Trim(),
                PasswordHash = PasswordHasher.HashBcrypt(dto.Password),
                CreatedAt = DateTime.UtcNow,
            };
            await _users.AddAsync(owner);
            created = true;
        }

        // The same path a self-serve company takes: the Owner membership plus
        // the defaults a tenant needs (leave types, a policy, a project, a team).
        var org = await _organizationService.CreateAsync(new CreateOrganizationDto { Name = dto.OrgName }, owner.Id);

        var addons = new List<string>();
        if (dto.Claims) addons.Add("expense_claim");
        if (dto.Attendance) addons.Add("clock");
        var isExpert = string.Equals(dto.Plan, "EXPERT", StringComparison.OrdinalIgnoreCase);
        await _organizationService.UpdatePlanAsync(org.Id, new UpdateOrgPlanDto
        {
            Plan = isExpert ? "EXPERT" : "DIY",
            // EXPERT has no tier split.
            Tier = isExpert ? null : (dto.Tier ?? "PAID"),
            Addons = addons,
        });

        // Into the NEW company's log, masked like every support action.
        if (_currentUser.UserId is { } supportUserId)
        {
            await _audit.WriteAsync(new AuditEvent(
                AuditActions.SupportCompanyCreate,
                $"Fusioneta support set up this company for {email}",
                TargetType: "Organization",
                TargetId: org.Id,
                OrganizationId: org.Id,
                Support: new SupportActor(supportUserId, _currentUser.Email ?? string.Empty)));
        }

        return new CreateSupportCompanyResultDto
        {
            OrganizationId = org.Id,
            OrganizationName = org.Name,
            OwnerCreated = created,
        };
    }

    public async Task<SupportOrganizationDto?> DeactivateAsync(string organizationId, string reason)
    {
        var org = await _organizations.GetByIdAsync(organizationId);
        if (org is null) return null;

        // A company Fusioneta's own superadmins sign in to can't be closed:
        // they'd be shut out with it, and the Reactivate button with them.
        if (organizationId == _currentUser.OrganizationId)
            throw new ArgumentException("This is the company you're signed in to, so it can't be deactivated.");
        if (_superadmins is not null)
        {
            var members = (await _memberships.GetAcrossAllOrgsAsync())
                .Where(m => m.OrganizationId == organizationId)
                .Select(m => m.UserId)
                .ToHashSet(StringComparer.Ordinal);
            var superadmin = (await _users.GetAllAsync())
                .FirstOrDefault(u => members.Contains(u.Id) && _superadmins.IsSuperadmin(u.Email));
            if (superadmin is not null)
                throw new ArgumentException(
                    $"{superadmin.Email} is a Fusioneta superadmin in this company, so it can't be deactivated.");
        }

        if (!org.IsDeactivated)
        {
            org.DeactivatedAt = DateTime.UtcNow;
            org.DeactivatedReason = reason.Trim();
            org.DeactivatedBy = _currentUser.Email;
            await _organizations.UpdateAsync(org);
            await ChangedAsync(org, AuditActions.SupportCompanyDeactivate, "Fusioneta support deactivated this company", deactivated: true);
        }
        return await RowAsync(org.Id);
    }

    public async Task<SupportOrganizationDto?> ReactivateAsync(string organizationId)
    {
        var org = await _organizations.GetByIdAsync(organizationId);
        if (org is null) return null;
        if (org.IsDeactivated)
        {
            var reason = org.DeactivatedReason;
            org.DeactivatedAt = null;
            org.DeactivatedReason = null;
            org.DeactivatedBy = null;
            await _organizations.UpdateAsync(org);
            await ChangedAsync(org, AuditActions.SupportCompanyReactivate, "Fusioneta support reactivated this company", deactivated: false, reason);
        }
        return await RowAsync(org.Id);
    }

    // The change bites at once, is in the company's log (as "System (Support)",
    // the person in the internal support log; the reason in neither) and goes
    // to the team chat.
    private async Task ChangedAsync(
        Organizations.Entities.Organization org, string action, string summary, bool deactivated, string? previousReason = null)
    {
        _status?.Changed();

        if (_currentUser.UserId is { } supportUserId)
        {
            await _audit.WriteAsync(new AuditEvent(
                action,
                summary,
                TargetType: "Organization",
                TargetId: org.Id,
                OrganizationId: org.Id,
                Support: new SupportActor(supportUserId, _currentUser.Email ?? string.Empty)));
        }

        if (_teamChat is not null)
            await _teamChat.CompanyStatusChangedAsync(org.Id, deactivated, deactivated ? org.DeactivatedReason : previousReason);
    }

    private async Task<SupportOrganizationDto?> RowAsync(string organizationId) =>
        (await ListOrganizationsAsync(null)).FirstOrDefault(r => r.Id == organizationId);
}
