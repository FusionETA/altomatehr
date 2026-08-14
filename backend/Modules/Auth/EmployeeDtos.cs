using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Auth;

// The admin's view of a user/employee (no password, ever).
public class EmployeeDto
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? SupervisorId { get; set; }
    public string? SupervisorEmail { get; set; }
    public string? PolicyId { get; set; }
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
}
