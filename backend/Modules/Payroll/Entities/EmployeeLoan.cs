using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// How the admin described the repayment when the loan was created.
//
//   FIXED  — "over N months"; the engine works out the monthly amount.
//   CUSTOM — "RM X a month"; the engine works out how many months.
//
// Either way the stored schedule is what actually gets deducted; this only
// records which question the admin answered.
public enum LoanRepaymentMode
{
    FIXED,
    CUSTOM,
}

public enum LoanStatus
{
    ACTIVE,
    COMPLETED,
    CANCELLED,
}

// A salary advance or staff loan, repaid by deduction from payroll.
//
// The schedule is stored rather than recomputed each run: an admin can vary a
// single installment (a smaller one in a lean month, a lump sum at bonus
// time), and a derived schedule would silently overwrite that.
public class EmployeeLoan : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)]
    public string EmployeeProfileId { get; set; } = string.Empty;

    [Precision(12, 2)]
    public decimal PrincipalAmount { get; set; }

    public LoanRepaymentMode Mode { get; set; } = LoanRepaymentMode.FIXED;

    // The regular monthly figure. The LAST installment may differ — it absorbs
    // whatever rounding left over — so this is the headline, not the truth.
    // `ScheduleJson` is the truth.
    [Precision(12, 2)]
    public decimal InstallmentAmount { get; set; }

    // The first period a deduction is taken. A loan granted mid-month usually
    // starts repaying the following month, so this is set explicitly rather
    // than derived from the creation date.
    public int StartYear { get; set; }
    public int StartMonth { get; set; }

    public int InstallmentCount { get; set; }

    public LoanStatus Status { get; set; } = LoanStatus.ACTIVE;

    [MaxLength(500)]
    public string? Notes { get; set; }

    // JSON array of per-installment amounts, one per period, in order. Null
    // falls back to an equal split — see PayrollLoans.ResolveSchedule.
    public string? ScheduleJson { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
