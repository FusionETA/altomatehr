using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Employees.Entities;

// One stretch of employment at an org — the monolith's EmploymentStint.
//
// The profile holds only the CURRENT join and leave dates, and a return
// (A → B → A, or a resignation then a restore) overwrites them. These rows keep
// what was overwritten: every tenure, how it started and how it ended. At most
// one OPEN row (LeaveDate null) per (org, user).
//
// Written when a tenure ends (archive, transfer out, the past-leaver sweep) or
// begins again (restore, transfer in). People who never left have no rows; the
// history read synthesises their current period from the profile.
public class EmploymentPeriod : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;

    // First day; null when the profile never had a join date.
    public DateTime? JoinDate { get; set; }

    // Last day; null = still open (current).
    public DateTime? LeaveDate { get; set; }

    // "Transferred from X", "Restored". Null = joined (unknown / first hire).
    [MaxLength(200)]
    public string? StartReason { get; set; }

    // "Transferred to X", "Resigned", "Leave date passed". Null while open.
    [MaxLength(200)]
    public string? EndReason { get; set; }

    // The EmployeeTransfer that opened / closed it, when one did.
    [MaxLength(40)]
    public string? OpenedByTransferId { get; set; }

    [MaxLength(40)]
    public string? ClosedByTransferId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
