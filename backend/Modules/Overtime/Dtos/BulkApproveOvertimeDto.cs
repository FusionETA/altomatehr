using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Overtime.Dtos;

// The overtime requests an approver is signing off in one gesture.
//
// There is deliberately no bulk REJECT counterpart. Rejection requires a
// remark, and one remark stapled to a dozen unrelated requests tells each
// employee nothing about why theirs was refused — so rejection stays one at a
// time, where the reason can be about that request.
public class BulkApproveOvertimeDto
{
    [Required, MinLength(1)]
    public List<string> Ids { get; set; } = [];
}
