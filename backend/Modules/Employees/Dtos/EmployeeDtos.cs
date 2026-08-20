using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Employees.Dtos;

// The admin's view of a user/employee (no password, ever).
public class EmployeeDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? SupervisorId { get; set; }
    public string? SupervisorEmail { get; set; }
    public string? PolicyId { get; set; }

    // Per-admin module grant. null = full access (no restriction).
    public IReadOnlyList<string>? Modules { get; set; }
}

// What an admin sends to change a user's role and/or assigned supervisor.
public class UpdateEmployeeDto
{
    [Required, MaxLength(20)]
    public string Role { get; set; } = "Employee";

    [MaxLength(40)]
    public string? SupervisorId { get; set; }   // null clears the assignment

    [MaxLength(40)]
    public string? PolicyId { get; set; }        // null → falls back to the org default policy

    // Per-admin module grant (subset of OrgModules keys). null = full access;
    // [] = locked out. Only meaningful for Admin members.
    public List<string>? Modules { get; set; }
}

// What an admin sends to ADD a member to their org. If the email already belongs
// to a user, that identity is REUSED (this is how the same person ends up in a
// second org); Password is only needed to create a brand-new login account.
public class CreateEmployeeDto
{
    [Required, EmailAddress, MaxLength(120)]
    public string Email { get; set; } = string.Empty;

    // Required only when the email is new (creating a fresh account). Ignored if
    // the account already exists — an existing person keeps their password.
    [MaxLength(100)]
    public string? Password { get; set; }

    [Required, MaxLength(20)]
    public string Role { get; set; } = "Employee";

    [MaxLength(40)]
    public string? SupervisorId { get; set; }

    [MaxLength(40)]
    public string? PolicyId { get; set; }

    // Per-admin module grant. null = full access; [] = locked out.
    public List<string>? Modules { get; set; }
}
