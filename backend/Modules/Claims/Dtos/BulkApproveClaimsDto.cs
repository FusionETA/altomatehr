using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Claims.Dtos;

// The claims an approver is signing off in one gesture.
//
// There is deliberately no bulk REJECT counterpart. Rejection requires a
// remark, and one remark stapled to a dozen unrelated claims tells each
// employee nothing about why theirs was refused — so rejection stays one at a
// time, where the reason can be about that claim.
public class BulkApproveClaimsDto
{
    [Required, MinLength(1)]
    public List<string> Ids { get; set; } = [];
}
