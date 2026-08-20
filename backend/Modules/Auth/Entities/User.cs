using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Auth.Entities;

// A login account — IDENTITY ONLY. Employment (which orgs, role, supervisor,
// policy) lives on OrganizationMembership, not here, because the same account
// can be an employee in one org and a supervisor in another. Password is stored
// HASHED, never in plaintext. This is not tenant-scoped — a user is global and
// reaches its orgs through memberships.
public class User
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(120)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(200)]
    public string PasswordHash { get; set; } = string.Empty;   // BCrypt hash — never the raw password

    public DateTime CreatedAt { get; set; }
}
