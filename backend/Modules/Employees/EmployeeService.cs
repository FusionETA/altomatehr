using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Modules.Employees;

// Admin management of employees in the ACTIVE org. An "employee" is a User with
// a membership in this org; role / supervisor / policy live on that membership,
// so the same person can be a plain employee here and a supervisor elsewhere.
// The membership repo is tenant-filtered, so an admin only sees/assigns within
// their own org.
public class EmployeeService : IEmployeeService
{
    // Canonical role names. Validated case-insensitively, stored canonically.
    private static readonly string[] AllowedRoles = ["Employee", "Supervisor", "Admin", "Owner"];

    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IUserRepository _users;
    private readonly ILeaveService _leave;

    public EmployeeService(
        IOrganizationMembershipRepository memberships,
        IUserRepository users,
        ILeaveService leave)
    {
        _memberships = memberships;
        _users = users;
        _leave = leave;
    }

    public async Task<IEnumerable<EmployeeDto>> GetAllAsync()
    {
        var members = await _memberships.GetForCurrentOrgAsync();
        var usersById = (await _users.GetAllAsync()).ToDictionary(u => u.Id);
        return members.Select(m => ToDto(m, usersById));
    }

    public async Task<EmployeeSaveResult> CreateAsync(CreateEmployeeDto dto)
    {
        var role = AllowedRoles.FirstOrDefault(r => string.Equals(r, dto.Role, StringComparison.OrdinalIgnoreCase));
        if (role is null)
            return new EmployeeSaveResult(false, null, $"Role must be one of: {string.Join(", ", AllowedRoles)}.");

        var email = dto.Email.Trim();
        if (email.Length == 0)
            return new EmployeeSaveResult(false, null, "Email is required.");

        // Reuse the account if the email already exists (this is the multi-org case —
        // the same identity gains a membership in another org); otherwise create a
        // fresh login account, which needs a password.
        var user = await _users.GetByEmailAsync(email);
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(dto.Password))
                return new EmployeeSaveResult(false, null, "A password is required to create a new account.");
            if (string.IsNullOrWhiteSpace(dto.Name))
                return new EmployeeSaveResult(false, null, "A name is required to create a new account.");

            user = new User
            {
                Email = email,
                Name = dto.Name.Trim(),
                PasswordHash = BC.HashPassword(dto.Password),
                CreatedAt = DateTime.UtcNow,
            };
            await _users.AddAsync(user);
        }

        // Already a member of THIS org? (unique per org, so we don't double-add.)
        if (await _memberships.GetForUserInCurrentOrgAsync(user.Id) is not null)
            return new EmployeeSaveResult(false, null, "This person is already a member of this organization.");

        var (modulesOk, modulesError, modulesCsv) = NormalizeModules(dto.Modules);
        if (!modulesOk)
            return new EmployeeSaveResult(false, null, modulesError);

        var membership = new OrganizationMembership
        {
            UserId = user.Id,
            Role = role,
            JoinDate = dto.JoinDate?.Date,
            PolicyId = string.IsNullOrWhiteSpace(dto.PolicyId) ? null : dto.PolicyId,
            ShiftId = string.IsNullOrWhiteSpace(dto.ShiftId) ? null : dto.ShiftId,
            Modules = modulesCsv,
            EmployeeNumber = string.IsNullOrWhiteSpace(dto.EmployeeNumber) ? null : dto.EmployeeNumber.Trim(),
            JobTitle = string.IsNullOrWhiteSpace(dto.JobTitle) ? null : dto.JobTitle.Trim(),
        };
        await _memberships.AddAsync(membership);   // StampTenant sets OrganizationId = the active org

        var usersById = (await _users.GetAllAsync()).ToDictionary(u => u.Id);
        return new EmployeeSaveResult(true, ToDto(membership, usersById), null);
    }

    public async Task<EmployeeSaveResult> UpdateAsync(string id, UpdateEmployeeDto dto)
    {
        var membership = await _memberships.GetForUserInCurrentOrgAsync(id);
        if (membership is null)
            return new EmployeeSaveResult(false, null, null);   // → 404 (not a member of this org)

        var role = AllowedRoles.FirstOrDefault(r => string.Equals(r, dto.Role, StringComparison.OrdinalIgnoreCase));
        if (role is null)
            return new EmployeeSaveResult(false, null, $"Role must be one of: {string.Join(", ", AllowedRoles)}.");

        var (modulesOk, modulesError, modulesCsv) = NormalizeModules(dto.Modules);
        if (!modulesOk)
            return new EmployeeSaveResult(false, null, modulesError);

        // Email is checked for uniqueness before anything is touched, so a
        // rejected change never leaves a partial write behind.
        if (dto.Email is not null)
        {
            var newEmail = dto.Email.Trim();
            if (newEmail.Length == 0)
                return new EmployeeSaveResult(false, null, "Email cannot be empty.");

            var existing = await _users.GetByEmailAsync(newEmail);
            if (existing is not null && existing.Id != id)
                return new EmployeeSaveResult(false, null, "That email is already in use.");
        }

        // Name and email live on the global User. Patch semantics: null → leave
        // unchanged (so an update that omits them can't wipe the person's identity).
        if (dto.Name is not null || dto.Email is not null)
        {
            var user = await _users.GetByIdAsync(id);
            if (user is not null)
            {
                if (dto.Name is not null) user.Name = dto.Name.Trim();
                if (dto.Email is not null) user.Email = dto.Email.Trim();
                await _users.UpdateAsync(user);
            }
        }

        membership.Role = role;
        membership.PolicyId = string.IsNullOrWhiteSpace(dto.PolicyId) ? null : dto.PolicyId;
        membership.ShiftId = string.IsNullOrWhiteSpace(dto.ShiftId) ? null : dto.ShiftId;
        membership.Modules = modulesCsv;
        // Same patch semantics as Name, and for the same reason. These
        // overwrote unconditionally, so a caller that sent only role and
        // supervisor — which is exactly what the employees table did — silently
        // wiped the person's employee number and job title.
        //
        // null → leave unchanged. Empty string → deliberately clear, so an
        // admin can still remove a job title on purpose.
        if (dto.EmployeeNumber is not null)
        {
            membership.EmployeeNumber = dto.EmployeeNumber.Trim() is { Length: > 0 } number
                ? number
                : null;
        }

        if (dto.JobTitle is not null)
        {
            membership.JobTitle = dto.JobTitle.Trim() is { Length: > 0 } title ? title : null;
        }

        // Setting or correcting the join date changes how much pro-rated leave
        // this person has earned, and nothing else would ever recalculate it —
        // the monthly cron only ever ADDS, never re-derives. Production hooks
        // the same trigger onto its employee save: "closes the 'I set joinDate
        // after hiring and the balance didn't move' gap."
        // Also patch semantics: omitting the join date used to null it, which
        // both lost the date AND triggered the accrual recompute below — so
        // changing somebody's role quietly rewrote their leave balance.
        var previousJoinDate = membership.JoinDate;
        if (dto.JoinDate is not null) membership.JoinDate = dto.JoinDate.Value.Date;
        var joinDateChanged = previousJoinDate != membership.JoinDate;
        await _memberships.UpdateAsync(membership);

        // Only after the membership is saved — the recompute reads JoinDate back.
        if (joinDateChanged)
            await _leave.RecomputeProRatedAccrualAsync(membership.UserId, DateTime.UtcNow.Year);

        var usersById = (await _users.GetAllAsync()).ToDictionary(u => u.Id);
        return new EmployeeSaveResult(true, ToDto(membership, usersById), null);
    }

    private static EmployeeDto ToDto(OrganizationMembership m, IReadOnlyDictionary<string, User> usersById) => new()
    {
        Id = m.UserId,
        Email = usersById.TryGetValue(m.UserId, out var user) ? user.Email : "",
        Name = user?.Name ?? "",
        AvatarUrl = user?.AvatarUrl,
        Role = m.Role,
        EmployeeNumber = m.EmployeeNumber,
        JobTitle = m.JobTitle,
        OtTimeBalanceMin = m.OtTimeBalanceMin,
        PolicyId = m.PolicyId,
        ShiftId = m.ShiftId,
        JoinDate = m.JoinDate,
        Modules = m.Modules is null ? null : OrgModules.Split(m.Modules),
    };

    // null grant → no restriction (stored as null). Otherwise every entry must be a known
    // module; an empty list is valid and means "locked out" (stored as "").
    private static (bool ok, string? error, string? csv) NormalizeModules(List<string>? modules)
    {
        if (modules is null) return (true, null, null);

        var cleaned = modules.Select(m => m.Trim()).Where(m => m.Length > 0).Distinct().ToList();
        var unknown = cleaned.Where(m => !OrgModules.IsKnownModule(m)).ToList();
        if (unknown.Count > 0)
            return (false, $"Unknown module(s): {string.Join(", ", unknown)}.", null);

        return (true, null, OrgModules.Join(cleaned));
    }
}
