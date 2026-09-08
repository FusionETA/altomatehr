using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Xero.Dtos;
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

    // Org-wide default working hours (HH:mm, 24h). Fallback used when an employee
    // has no assigned Shift and their project has no default Shift either.
    [MaxLength(5)]
    public string WorkingHoursStart { get; set; } = "09:00";

    [MaxLength(5)]
    public string WorkingHoursEnd { get; set; } = "18:00";

    // ---- Subscription / package (drives module access via OrgModules) ----
    // Plan/Tier/Addons together decide which modules the org is entitled to.
    public OrgPlan Plan { get; set; } = OrgPlan.DIY;

    // Only meaningful for DIY. Null for EXPERT.
    public OrgPlanTier? Tier { get; set; }

    // csv of addon keys ("expense_claim,clock"). Empty = no paid modules.
    [MaxLength(200)]
    public string Addons { get; set; } = string.Empty;

    // Day of month that closes the claims run. Claims submitted on or before it
    // belong to the current month's run; later ones still go through but fall
    // into the next run. Capped at 28 so every month actually has this day.
    public int ClaimRunCutoffDay { get; set; } = 25;

    // How approved claims get paid out, org-wide. Each claim is stamped with
    // this at creation (see Claim.Settlement) rather than reading the setting
    // live — changing the policy must not silently re-route claims that have
    // already been billed to Xero, which would pay them a second time.
    public ClaimSettlement ClaimSettlementRoute { get; set; } = ClaimSettlement.XERO_BILL;

    // Which stage a claim's bill lands at in Xero. AwaitingPayment is a live
    // payable the moment it arrives; Draft parks it in the accountant's queue to
    // be reviewed there first. Orgs that want a second pair of eyes on the
    // accounting side pick Draft.
    public XeroBillStatus XeroBillStage { get; set; } = XeroBillStatus.AwaitingPayment;

    // Org-wide default working days as a CSV of weekday numbers 1-7
    // (Mon = 1 … Sun = 7). Null means Mon-Fri. Leave counts only these days,
    // so a Fri-Mon request costs 2 days rather than 4.
    [MaxLength(20)]
    public string? WorkingDays { get; set; }

    public DateTime CreatedAt { get; set; }
}
