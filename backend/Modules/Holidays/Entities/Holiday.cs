using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Holidays.Entities;

// A public holiday. Org-wide when ProjectId is null; project-specific
// otherwise (e.g. a site that observes a state holiday the rest of the org
// doesn't). Mirrors the real app's split OrgHoliday / ProjectHoliday models,
// collapsed into one table here since the only difference is the scope.
//
// A date counts as a public holiday for an employee when there's either an
// org-wide row for that date, or a project-specific row for THEIR project.
public class Holiday : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    // Null = org-wide. Set = only observed by that project.
    [MaxLength(40)]
    public string? ProjectId { get; set; }

    // Date-only (stored as the UTC-midnight instant of the calendar date, the
    // same convention AttendanceRecord.Date uses).
    public DateTime Date { get; set; }

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
