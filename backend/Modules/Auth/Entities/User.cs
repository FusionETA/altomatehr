using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Auth.Entities;

// A real user, stored in the Users table. Password is stored HASHED, never in plaintext.
public class User : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant this user belongs to

    [MaxLength(120)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(200)]
    public string PasswordHash { get; set; } = string.Empty;   // BCrypt hash — never the raw password

    [MaxLength(20)]
    public string Role { get; set; } = "Employee";   // Employee | Supervisor | Admin | Owner

    // The user's approving supervisor (another User in the same org). Null =
    // unassigned. Leave/claim approvals route to this person.
    [MaxLength(40)]
    public string? SupervisorId { get; set; }

    // The employee's assigned policy (rules bundle). Null = use the org default.
    [MaxLength(40)]
    public string? PolicyId { get; set; }

    public DateTime CreatedAt { get; set; }
}
