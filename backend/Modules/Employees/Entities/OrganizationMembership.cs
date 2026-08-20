using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Employees.Entities;

// Links a User to an Organization they belong to (many-to-many) and holds the
// PER-ORG profile: their role in THIS org, plus (for staff) their supervisor and
// policy in this org.
//
// Role is per membership, not global: a person can be a Supervisor in one org and
// a plain Employee in another — they're only a supervisor where they've been
// assigned. Admins are separate accounts, so an employee never becomes an admin.
public class OrganizationMembership : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    // Employee | Supervisor | Admin | Owner — the role IN THIS org. Default Employee.
    [MaxLength(20)]
    public string Role { get; set; } = "Employee";

    // The approving supervisor in THIS org (a UserId). Null = unassigned.
    [MaxLength(40)]
    public string? SupervisorId { get; set; }

    // The policy that governs this person IN THIS org. Null = use the org default.
    [MaxLength(40)]
    public string? PolicyId { get; set; }

    // Per-admin module grant (csv of OrgModules keys) — narrows what THIS admin can access
    // below the org's plan ceiling. null = no restriction (full access, e.g. owners).
    // Empty string = locked out. Ignored for non-admins.
    [MaxLength(300)]
    public string? Modules { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
