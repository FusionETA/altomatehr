using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Email;
using Microsoft.Extensions.Options;
using AltomateHR.Api.Modules.Leave;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using BC = BCrypt.Net.BCrypt;

using AltomateHR.Api.Modules.Audit;

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
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IUserRepository _users;
    private readonly IAuditService _audit;
    private readonly ILeaveService _leave;
    private readonly ICurrentUser _currentUser;
    private readonly IEmailSender _email;
    private readonly IOrganizationRepository _organizations;
    private readonly Teams.IApproverPositions _approverPositions;
    private readonly PortalOptions _portal;

    public EmployeeService(
        IOrganizationMembershipRepository memberships,
        IEmployeeProfileRepository profiles,
        IUserRepository users,
        ILeaveService leave,
        IAuditService audit,
        ICurrentUser currentUser,
        IEmailSender email,
        IOrganizationRepository organizations,
        Teams.IApproverPositions approverPositions,
        IOptions<PortalOptions> portal)
    {
        _memberships = memberships;
        _profiles = profiles;
        _users = users;
        _leave = leave;
        _audit = audit;
        _currentUser = currentUser;
        _email = email;
        _organizations = organizations;
        _approverPositions = approverPositions;
        _portal = portal.Value;
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
        var passwordMode = WelcomeEmail.PasswordMode.Existing;

        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return new EmployeeSaveResult(false, null, "A name is required to create a new account.");

            // The house convention — email + birthday as MMDD — unless the
            // admin typed one. Derived rather than random so the welcome email
            // can state the RULE and never the credential itself; that is the
            // only thing making a guessable password a fair trade, so the two
            // have to move together.
            var password = dto.Password;
            if (string.IsNullOrWhiteSpace(password))
            {
                password = DefaultPassword.For(email, dto.DateOfBirth);
                if (password is null)
                {
                    return new EmployeeSaveResult(false, null,
                        "A date of birth is required to create a new account — the first password "
                        + "is the email followed by the birthday as MMDD. Type a password instead "
                        + "if you'd rather set one.");
                }
                passwordMode = WelcomeEmail.PasswordMode.Default;
            }
            else
            {
                passwordMode = WelcomeEmail.PasswordMode.Manual;
            }

            user = new User
            {
                Email = email,
                Name = dto.Name.Trim(),
                PasswordHash = BC.HashPassword(password),
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

        await SyncProfileDatesAsync(user.Id, dto.JoinDate, dto.DateOfBirth);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.EmployeeCreate,
            $"{dto.Email} as {membership.Role}",
            TargetType: "Employee",
            TargetId: membership.UserId,
            Metadata: new { dto.Email, membership.Role }));

        var usersById = (await _users.GetAllAsync()).ToDictionary(u => u.Id);

        // Best-effort, and deliberately last: someone who was added must not be
        // reported as a failure because the mail server was down. The caller
        // says "added, but the email didn't go out".
        bool? welcomeSent = null;
        if (dto.SendWelcomeEmail)
        {
            welcomeSent = await TrySendWelcomeAsync(user, passwordMode);
        }

        return new EmployeeSaveResult(true, ToDto(membership, usersById), null, welcomeSent);
    }

    private async Task<bool> TrySendWelcomeAsync(User user, WelcomeEmail.PasswordMode mode)
    {
        try
        {
            var org = await _organizations.GetByIdAsync(_currentUser.OrganizationId ?? "");
            var html = WelcomeEmail.BuildHtml(
                user.Name,
                org?.Name ?? "AltomateHR",
                user.Email,
                _portal.LoginUrl,
                mode);

            return await _email.SendAsync(
                user.Email, $"Welcome to {org?.Name ?? "AltomateHR"} — your AltomateHR account", html);
        }
        catch
        {
            // Never rethrow: the account exists either way, and an exception
            // here would roll a successful create into a 500.
            return false;
        }
    }

    // Names the change rather than saying "updated": a feed of twenty identical
    // "Employee updated" lines is a feed nobody reads.
    // A sentence an admin can read in the activity log. This used to be the
    // user's GUID (plus "— role X → Y"), which named nobody: the id is already
    // on the entry as TargetId for anything that needs to follow it.
    private static string DescribeEmployeeChange(string who, string? fromRole, string toRole) =>
        string.Equals(fromRole, toRole, StringComparison.Ordinal)
            ? $"Updated {who}'s details"
            : $"Changed {who}'s role from {fromRole ?? "none"} to {toRole}";

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

        // The other half of the rule Teams enforces on placement. Without this
        // the guard is a formality: place someone correctly as a Supervisor,
        // then demote them here, and they are back to being an approver the
        // approvals screens will not admit — with their team's requests already
        // routing to them.
        //
        // Admin and Owner are exempt for the same reason they are exempt there:
        // approval routing subtracts them outright, so they are never anybody's
        // approver no matter which layer they sit in.
        if (string.Equals(role, OrgRoles.Employee, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(membership.Role, OrgRoles.Employee, StringComparison.OrdinalIgnoreCase))
        {
            var positions = await _approverPositions.ForEmployeeAsync(id);
            if (positions.Count > 0)
            {
                var teams = string.Join(", ", positions.Select(p => $"\"{p.TeamName}\""));
                return new EmployeeSaveResult(false, null,
                    "They cannot be made an Employee while they sit above the bottom layer in "
                    + $"{teams} — the people below them route requests to them, and an Employee "
                    + "cannot open the approvals screens. Move them down a layer, or off those "
                    + "teams, first.");
            }
        }

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

        // Read before the overwrite: a role change is the one thing on this
        // form that alters who can approve whom, and "what was it before" is
        // the question that gets asked afterwards.
        var previousRole = membership.Role;

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

        // Null birthday: the edit form doesn't carry one, and null means
        // "leave unchanged" rather than clear it.
        await SyncProfileDatesAsync(membership.UserId, dto.JoinDate, null);

        // The one field on this form that decides what this person can
        // approve.
        var roleChanged = !string.Equals(previousRole, membership.Role, StringComparison.Ordinal);

        var target = await _users.GetByIdAsync(membership.UserId);
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.EmployeeUpdate,
            DescribeEmployeeChange(
                PersonName.Display(target?.Name, target?.Email, "an employee"),
                previousRole,
                membership.Role),
            TargetType: "Employee",
            TargetId: membership.UserId,
            Metadata: new
            {
                Role = new { From = previousRole, To = membership.Role, Changed = roleChanged },
                membership.PolicyId,
                membership.ShiftId,
            }));

        // Only after the membership is saved — the recompute reads JoinDate back.
        if (joinDateChanged)
            await _leave.RecomputeProRatedAccrualAsync(membership.UserId, DateTime.UtcNow.Year);

        var usersById = (await _users.GetAllAsync()).ToDictionary(u => u.Id);
        return new EmployeeSaveResult(true, ToDto(membership, usersById), null);
    }

    // The join date is stored TWICE: on the membership, which pro-rates leave
    // accrual, and on the EmployeeProfile, which is what payroll readiness
    // gates on (PayrollProfileReadiness.IsEmploymentComplete). Writing only the
    // membership left every employee added here reading "Employment
    // incomplete" however carefully the admin filled the date in — the profile
    // editor quietly reconciled the two on its first save, which is why this
    // went unnoticed until employees arrived by the hundred through an import.
    //
    // Creates the profile row when there is none, the same way the payroll
    // employees import does: a member without one is a roster waiting to be
    // filled in, not an error.
    private async Task SyncProfileDatesAsync(
        string userId, DateTime? joinDate, DateTime? dateOfBirth)
    {
        // Null means "leave unchanged" for both, exactly as it does for the
        // membership above — neither must clear a date the profile already has.
        if (joinDate is null && dateOfBirth is null) return;

        var profile = await _profiles.GetByUserAsync(userId);
        if (profile is null)
        {
            await _profiles.AddAsync(new EmployeeProfile
            {
                UserId = userId,
                JoinDate = joinDate?.Date,
                // Kept because the first password is derived from it. Asking
                // for a birthday to build a password and then discarding it
                // would make the payroll import ask for the same date again.
                DateOfBirth = dateOfBirth?.Date,
            });
            return;
        }

        var changed = false;
        if (joinDate is not null && profile.JoinDate != joinDate.Value.Date)
        {
            profile.JoinDate = joinDate.Value.Date;
            changed = true;
        }
        if (dateOfBirth is not null && profile.DateOfBirth != dateOfBirth.Value.Date)
        {
            profile.DateOfBirth = dateOfBirth.Value.Date;
            changed = true;
        }
        if (!changed) return;

        profile.UpdatedAt = DateTime.UtcNow;
        await _profiles.UpdateAsync(profile);
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

    // Overwrite an employee's login password with one the admin types in.
    //
    // The self-service reset sends a code to the address on file, which is no
    // help to someone who has lost access to that address — a returning
    // employee, or one whose personal email is gone. This is the way back in.
    //
    // Role is gated at the controller (Admin/Owner). The rest of the
    // guardrails live here, where they can see the data:
    public async Task<SetPasswordResult> SetPasswordAsync(string userId, string newPassword)
    {
        // Matches the forgot-password policy, so the two routes can't disagree
        // about what an acceptable password is.
        if ((newPassword ?? string.Empty).Length < 8)
            return new SetPasswordResult(false, "Password must be at least 8 characters.");

        // Changing your own password here would replace the credential backing
        // the session you are using, mid-request. Sign out and use the reset
        // flow, which is built for it.
        if (userId == _currentUser.UserId)
        {
            return new SetPasswordResult(false,
                "You cannot change your own password here — sign out and use Forgot password.");
        }

        // Scoped by membership in the CALLER'S ACTIVE ORG, not the target's
        // home org: someone who belongs to three companies must stay editable
        // by an admin of any of them, and only for the one they are in.
        var membership = await _memberships.GetForUserInCurrentOrgAsync(userId);
        if (membership is null) return new SetPasswordResult(false, null);   // → 404

        // Owner accounts are the top of the trust chain. An admin resetting an
        // owner's password could take the company; that belongs to a support
        // flow with its own checks, not a per-employee button.
        if (string.Equals(membership.Role, "Owner", StringComparison.OrdinalIgnoreCase))
        {
            return new SetPasswordResult(false,
                "Owner accounts cannot be changed from here. Contact support.");
        }

        var user = await _users.GetByIdAsync(userId);
        if (user is null) return new SetPasswordResult(false, null);

        user.PasswordHash = BC.HashPassword(newPassword);
        await _users.UpdateAsync(user);

        // The password itself is never recorded — only that it changed, and
        // who to. An audit trail that leaks the credential is worse than none.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.UserPasswordAdminSet,
            $"Set a new password for {user.Email}.",
            TargetType: "User",
            TargetId: user.Id,
            Metadata: new { TargetEmail = user.Email, TargetRole = membership.Role }));

        return new SetPasswordResult(true, null);
    }
}
