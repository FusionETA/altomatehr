using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Organizations.Dtos;

// Provision or change a tenant's package. NOTE: in production this belongs to a partner /
// billing surface, not a self-serve owner action — it's Owner-gated here for dev only.
public class UpdateOrgPlanDto
{
    [Required, MaxLength(20)]
    public string Plan { get; set; } = "DIY";        // DIY | EXPERT

    [MaxLength(20)]
    public string? Tier { get; set; }                // FREE | PAID (only meaningful for DIY)

    public List<string> Addons { get; set; } = new(); // subset of OrgModules.AllAddons
}
