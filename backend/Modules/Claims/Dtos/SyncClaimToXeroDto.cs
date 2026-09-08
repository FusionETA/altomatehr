using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Xero.Dtos;

namespace AltomateHR.Api.Modules.Claims.Dtos;

// What state the caller wants the bill to land in — or nothing, to use the
// org's configured stage.
//
// NULLABLE on purpose. It used to default to AwaitingPayment, which made "the
// caller didn't say" indistinguishable from "the caller asked for a live
// payable" — so a request with an empty body would silently override the org's
// Xero bill stage setting. Null now means "use the setting", which
// ClaimsService.SyncToXeroAsync resolves.
public class SyncClaimToXeroDto
{
    public XeroBillStatus? Status { get; set; }
}

// The bulk form: the same stage choice, applied to a set of claims.
public class BulkSyncClaimsToXeroDto
{
    [Required, MinLength(1)]
    public List<string> Ids { get; set; } = [];

    public XeroBillStatus? Status { get; set; }
}
