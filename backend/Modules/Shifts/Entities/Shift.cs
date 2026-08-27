using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Shifts.Entities;

// A named working schedule ("Day shift", "Night shift") scoped to one project.
// Exactly one shift per project is the default (used for employees on that
// project who don't have a shift explicitly assigned). Name uniqueness (within
// a project) and the "exactly one default" invariant are enforced in the
// service layer, matching how Modules/Policies/EmployeePolicy does it.
public class Shift : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string ProjectId { get; set; } = string.Empty;        // FK → Projects. Immutable after creation.

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(5)]
    public string StartTime { get; set; } = string.Empty;        // HH:mm, 24h

    [MaxLength(5)]
    public string EndTime { get; set; } = string.Empty;          // HH:mm, 24h

    // Comma-separated ISO weekday numbers (1=Mon..7=Sun), e.g. "1,2,3,4,5".
    // Null means every day.
    [MaxLength(20)]
    public string? WorkingDays { get; set; }

    public int LunchBreakMinutes { get; set; } = 60;

    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
