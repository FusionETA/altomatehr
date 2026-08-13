using AltomateHR.Api.Modules.Auth.Entities;

namespace AltomateHR.Api.Modules.Auth;

// Admin management of users/employees: list them and set each one's role +
// approving supervisor. All lookups are org-scoped by the global query filter,
// so an admin can only see and assign within their own org.
public class EmployeeService : IEmployeeService
{
    // Canonical role names. Assignment is validated case-insensitively and
    // stored in canonical casing.
    private static readonly string[] AllowedRoles = ["Employee", "Supervisor", "Admin", "Owner"];

    private readonly IUserRepository _users;

    public EmployeeService(IUserRepository users) => _users = users;

    public async Task<IEnumerable<EmployeeDto>> GetAllAsync()
    {
        var users = await _users.GetAllAsync();
        var emailById = users.ToDictionary(u => u.Id, u => u.Email);
        return users.Select(u => ToDto(u, emailById));
    }

    public async Task<EmployeeSaveResult> UpdateAsync(string id, UpdateEmployeeDto dto)
    {
        var user = await _users.GetByIdAsync(id);
        if (user is null)
            return new EmployeeSaveResult(false, null, null);   // → 404

        var role = AllowedRoles.FirstOrDefault(r => string.Equals(r, dto.Role, StringComparison.OrdinalIgnoreCase));
        if (role is null)
            return new EmployeeSaveResult(false, null, $"Role must be one of: {string.Join(", ", AllowedRoles)}.");

        var supervisorId = string.IsNullOrWhiteSpace(dto.SupervisorId) ? null : dto.SupervisorId;
        if (supervisorId is not null)
        {
            if (supervisorId == id)
                return new EmployeeSaveResult(false, null, "A user can't be their own supervisor.");

            var supervisor = await _users.GetByIdAsync(supervisorId);
            if (supervisor is null)   // GetByIdAsync is org-scoped → also blocks cross-org assignment
                return new EmployeeSaveResult(false, null, "The chosen supervisor doesn't exist in this organization.");
        }

        user.Role = role;
        user.SupervisorId = supervisorId;
        await _users.UpdateAsync(user);

        var emailById = (await _users.GetAllAsync()).ToDictionary(u => u.Id, u => u.Email);
        return new EmployeeSaveResult(true, ToDto(user, emailById), null);
    }

    private static EmployeeDto ToDto(User u, IReadOnlyDictionary<string, string> emailById) => new()
    {
        Id = u.Id,
        Email = u.Email,
        Role = u.Role,
        SupervisorId = u.SupervisorId,
        SupervisorEmail = u.SupervisorId is not null && emailById.TryGetValue(u.SupervisorId, out var email)
            ? email
            : null,
    };
}
