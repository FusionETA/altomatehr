using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Xero.Dtos;

namespace AltomateHR.Api.Modules.Claims.Dtos;

// The claims module's own settings surface. A separate shape from
// UpdateOrganizationDto on purpose: the org settings screen PUTs its whole form,
// so folding these in there would mean the claims screen had to send (and could
// silently clobber) the working hours, currency and geofence radius alongside.
public class ClaimSettingsDto
{
    public int ClaimRunCutoffDay { get; set; }

    // How approved claims are paid out. Set once for the org rather than chosen
    // per claim: it is an accounting policy, not a per-receipt judgement.
    public ClaimSettlement SettlementRoute { get; set; } = ClaimSettlement.XERO_BILL;

    // Which stage a bill lands at in Xero. Only meaningful on the XERO_BILL
    // route — nothing reaches Xero on the payroll route.
    public XeroBillStatus XeroBillStage { get; set; } = XeroBillStatus.AwaitingPayment;
}

public class UpdateClaimSettingsDto
{
    // 1-28 rather than 1-31: see OrganizationService.SetClaimSettingsAsync for
    // why a cutoff that doesn't exist in February is worth refusing.
    [Range(1, 28, ErrorMessage = "Cutoff day must be between 1 and 28.")]
    public int ClaimRunCutoffDay { get; set; } = 25;

    public ClaimSettlement SettlementRoute { get; set; } = ClaimSettlement.XERO_BILL;

    public XeroBillStatus XeroBillStage { get; set; } = XeroBillStatus.AwaitingPayment;
}
