using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// The employees an admin CHOSE to include when starting a run.
//
// Generation is destructive and pulls from the live roster, so "who is on this
// run" cannot live on the payslips alone — a regeneration would rebuild the
// whole roster and lose the admin's selection. This is that selection, frozen
// at create time: one row per included employee. Generation intersects the
// current roster with these rows, then still applies SkipReasonFor as the final
// eligibility gate (someone chosen in March who is archived by the time it runs
// is skipped, not paid).
//
// A run with NO member rows is a legacy or imported run created before the
// picker existed; generation treats that as "everyone", preserving old behaviour.
public class PayrollRunMember : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)]
    public string PayrollRunId { get; set; } = string.Empty;

    [MaxLength(40)]
    public string EmployeeProfileId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
