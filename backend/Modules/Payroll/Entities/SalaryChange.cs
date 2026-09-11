using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Policies.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// Why a salary moved. A typo correction is deliberately NOT one of these —
// fixing a mistyped figure is not a change to what someone earns, and
// recording it as one would corrupt the very history this table exists for.
public enum SalaryChangeReason
{
    RAISE,
    PROMOTION,
    DEMOTION,
    RESTRUCTURE,
    OTHER,
}

// One legitimate change to an employee's pay, with both sides recorded.
//
// The history is the point. It gets asked for by:
//   · LHDN, SOCSO and EIS, when a contribution is queried
//   · an Industrial Relations dispute
//   · a retrenchment or VSS payout, which turns on "last drawn salary"
//   · the employee themselves, for a loan or an income letter
//
// Both the previous and the new values are stored on the row rather than
// derived by walking the chain: the profile can be edited by hand, and a
// history that has to be reconstructed from the current state is not a
// history.
public class SalaryChange : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)]
    public string EmployeeProfileId { get; set; } = string.Empty;

    // The date the new salary takes effect, INCLUSIVE — a change effective
    // the 15th means the 15th is paid at the new rate.
    public DateTime EffectiveDate { get; set; }

    // ---- Before ----
    public SalaryType PreviousSalaryType { get; set; } = SalaryType.MONTHLY;
    [Precision(12, 2)] public decimal? PreviousMonthlySalary { get; set; }
    [Precision(12, 2)] public decimal? PreviousHourlyRate { get; set; }

    // ---- After ----
    public SalaryType NewSalaryType { get; set; } = SalaryType.MONTHLY;
    [Precision(12, 2)] public decimal? NewMonthlySalary { get; set; }
    [Precision(12, 2)] public decimal? NewHourlyRate { get; set; }

    public SalaryChangeReason Reason { get; set; } = SalaryChangeReason.RAISE;

    [MaxLength(500)]
    public string? Notes { get; set; }

    // Who recorded it. Null on rows created before this was captured, or by
    // a migration.
    [MaxLength(40)]
    public string? ChangedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
