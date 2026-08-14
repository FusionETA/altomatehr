using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Teams.Entities;

// An employee's membership in a team, at a given hierarchy layer (0-based,
// bottom = 0). One row per (team, employee).
public class TeamMembership : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string TeamId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;   // User id

    public int Layer { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
