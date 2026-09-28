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

    public SupportService(
        IOrganizationRepository organizations,
        IOrganizationService organizationService,
        IOrganizationMembershipRepository memberships,
        IUserRepository users,
        IAuditService audit,
        ICurrentUser currentUser)
    {
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
}
