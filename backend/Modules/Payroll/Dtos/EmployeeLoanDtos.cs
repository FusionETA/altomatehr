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
}

public class LoanInstallmentDto
{
    public int Index { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public bool Paid { get; set; }
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
