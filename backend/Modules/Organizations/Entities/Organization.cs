using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Organizations.Entities;

// The tenant. Every tenant-scoped entity (User, Claim, …) carries this org's Id.
// Organization itself is NOT tenant-scoped (it's the top of the tree).
public class Organization
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    // Org-level defaults that settings/modules read (expanded as we build settings).
    [MaxLength(3)]
    public string DefaultCurrency { get; set; } = "MYR";

    [Precision(10, 4)]
    public decimal DefaultMileageRate { get; set; }

    public MileageUnit MileageUnit { get; set; } = MileageUnit.KM;

    // How far (metres) from a project's geofence centre still counts as on-site.
    public int GeofenceRadiusMeters { get; set; } = 200;

    // ---- Subscription / package (drives module access via OrgModules) ----
    // Plan/Tier/Addons together decide which modules the org is entitled to.
    public OrgPlan Plan { get; set; } = OrgPlan.DIY;

    // Only meaningful for DIY. Null for EXPERT.
    public OrgPlanTier? Tier { get; set; }

    // csv of addon keys ("expense_claim,clock"). Empty = no paid modules.
    [MaxLength(200)]
    public string Addons { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
