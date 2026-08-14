using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Teams.Entities;

// A team within a project. A team has `LayerCount` hierarchy layers (named by
// LayerLabels); members sit at a layer, and the approval chain (B2) escalates
// up the layers. The monolith's per-event approval gates + shift config land
// with the attendance-details pass.
public class Team : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant — auto-stamped + auto-filtered

    [MaxLength(40)]
    public string ProjectId { get; set; } = string.Empty;        // FK → Projects

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public int LayerCount { get; set; } = 1;

    // JSON array of layer labels bottom→top, e.g. ["Staff","Team Lead","Manager"].
    // Stored as a string to avoid an EF collection converter; the service maps it.
    public string LayerLabels { get; set; } = "[]";

    // Which layers must approve, per module. JSON object keyed by ApprovalModule
    // name → array of layer indices, e.g. {"CLAIMS":[0,1],"LEAVE":[0]}. A module
    // absent from the map means "all layers approve" (the default); a module
    // present with an empty array means "skip approval entirely".
    public string ModuleApprovalConfig { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
