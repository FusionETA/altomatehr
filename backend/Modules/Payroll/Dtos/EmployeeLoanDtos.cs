using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll.Dtos;

public class EmployeeLoanDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeProfileId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;

    public decimal PrincipalAmount { get; set; }
    public LoanRepaymentMode Mode { get; set; }
    public decimal InstallmentAmount { get; set; }
    public int StartYear { get; set; }
    public int StartMonth { get; set; }
    public int InstallmentCount { get; set; }
    public LoanStatus Status { get; set; }
    public string? Notes { get; set; }

    // The per-period schedule, with each installment marked paid once its
    // period has a submitted run.
    public IReadOnlyList<LoanInstallmentDto> Schedule { get; set; } = [];

    public int PaidInstallments { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public int EndYear { get; set; }
    public int EndMonth { get; set; }
    public bool FullyRepaid { get; set; }

    // Once a loan has started repaying, editing its terms would restate
    // months already filed. The UI locks the form on this.
    public bool HasStarted { get; set; }

    // Set while PAUSED: the first month that deducts nothing.
    public int? PausedFromYear { get; set; }
    public int? PausedFromMonth { get; set; }

    // The earliest month a re-plan, skip or pause can touch — the month after
    // the last one with a submitted or awaiting-approval run.
    public int FirstEditableYear { get; set; }
    public int FirstEditableMonth { get; set; }

    // What a re-plan has to spread: the principal less every locked installment.
    public decimal RemainingToPlan { get; set; }

    // Advisory: repayments past the leaving date, loan deductions over half the
    // salary. The plan is saved regardless.
    public IReadOnlyList<string> Warnings { get; set; } = [];
}

public class LoanInstallmentDto
{
    public int Index { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool Paid { get; set; }

    // Under a pause that has not been resumed yet — nothing is taken.
    public bool Paused { get; set; }

    // Its month is submitted or awaiting approval, so it cannot change.
    public bool Locked { get; set; }
}

public class SaveEmployeeLoanDto
{
    [Required, MaxLength(40)]
    public string EmployeeProfileId { get; set; } = string.Empty;

    [Range(0.01, 10_000_000)]
    public decimal PrincipalAmount { get; set; }

    public LoanRepaymentMode Mode { get; set; } = LoanRepaymentMode.FIXED;

    // FIXED reads InstallmentCount; CUSTOM reads InstallmentAmount. The
    // service works out the other and refuses if the one it needs is missing.
    [Range(1, 600)]
    public int? InstallmentCount { get; set; }

    [Range(0.01, 10_000_000)]
    public decimal? InstallmentAmount { get; set; }

    [Range(2000, 2100)]
    public int StartYear { get; set; }

    [Range(1, 12)]
    public int StartMonth { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    // A hand-varied schedule — a lighter month, a lump sum at bonus time.
    // Must add up to the principal. Omitted means an equal split.
    public IReadOnlyList<decimal>? Schedule { get; set; }
}

// Re-plan what a started loan still owes. Locked months stay as they are; the
// balance is spread one of three ways, exactly as when recording a loan:
// FIXED over InstallmentCount months, CUSTOM at InstallmentAmount a month, or
// Remainder typed month by month. Once a loan has started, re-planning makes
// it CUSTOM — "over N months" no longer describes it.
public class ReplanLoanDto
{
    public LoanRepaymentMode Mode { get; set; } = LoanRepaymentMode.FIXED;

    [Range(1, 600)]
    public int? InstallmentCount { get; set; }

    [Range(0.01, 10_000_000)]
    public decimal? InstallmentAmount { get; set; }

    // Amounts for the months from FirstEditable onwards. Must add up to
    // RemainingToPlan.
    public IReadOnlyList<decimal>? Remainder { get; set; }
}

// Skip a run of months: RM 0 each, with every later installment moved back.
public class SkipLoanMonthsDto
{
    [Range(2000, 2100)]
    public int FromYear { get; set; }

    [Range(1, 12)]
    public int FromMonth { get; set; }

    [Range(1, 24)]
    public int Months { get; set; } = 1;
}

// Pause from a month until an admin resumes it.
public class PauseLoanDto
{
    [Range(2000, 2100)]
    public int FromYear { get; set; }

    [Range(1, 12)]
    public int FromMonth { get; set; }
}

// Resume a paused loan: this month deducts again.
public class ResumeLoanDto
{
    [Range(2000, 2100)]
    public int Year { get; set; }

    [Range(1, 12)]
    public int Month { get; set; }
}
