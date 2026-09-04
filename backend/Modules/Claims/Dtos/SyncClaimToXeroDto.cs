using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Xero.Dtos;

namespace AltomateHR.Api.Modules.Claims.Dtos;

// What state the admin wants the bill to land in.
//
// Defaults to AwaitingPayment: the claim has already cleared its approval
// chain, so the org genuinely owes the money and a live payable is the honest
// record. Draft is there for finance teams who want a second pair of eyes in
// Xero before it counts.
public class SyncClaimToXeroDto
{
    public XeroBillStatus Status { get; set; } = XeroBillStatus.AwaitingPayment;
}

// The bulk form: the same stage choice, applied to a set of claims.
public class BulkSyncClaimsToXeroDto
{
    [Required, MinLength(1)]
    public List<string> Ids { get; set; } = [];

    public XeroBillStatus Status { get; set; } = XeroBillStatus.AwaitingPayment;
}
