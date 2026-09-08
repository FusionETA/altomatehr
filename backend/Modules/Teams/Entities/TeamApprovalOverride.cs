using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Teams.Entities;

// An explicit, admin-picked override of who approves for ONE employee at ONE
// layer of a team — as opposed to the implicit default (everyone else who
// happens to sit at that layer). Only exists where an admin deliberately
// deviated from the default; its absence for a given (team, employee, layer)
// means "use the default."
public class TeamApprovalOverride : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string TeamId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string EmployeeId { get; set; } = string.Empty;   // whose chain this overrides

    public int Layer { get; set; }                            // which layer's approvers are overridden

    // JSON array of approver user ids. "[]" = deliberately nobody at this
    // layer for THIS employee — distinct from no row existing at all, which
    // means "fall back to the team's default: everyone else at this layer."
    public string ApproverIdsJson { get; set; } = "[]";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
