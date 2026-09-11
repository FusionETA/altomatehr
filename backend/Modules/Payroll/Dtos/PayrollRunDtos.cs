using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType

namespace AltomateHR.Api.Modules.Payroll.Dtos;

// Start a run for a period. Id, status, totals and timestamps are the server's
// to set — a client that could name them could backdate a filing.
public class CreatePayrollRunDto
{
    [Range(2000, 2100)]
    public int PeriodYear { get; set; }

    [Range(1, 12)]
    public int PeriodMonth { get; set; }
}

// A run in the list. Totals only — the payslips come with the detail.
public class PayrollRunDto
{
    public string Id { get; set; } = string.Empty;

    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }

    // "January 2026" — rendered server-side so the month names cannot drift
    // between the run list, the payslip PDF and the submission files.
    public string PeriodLabel { get; set; } = string.Empty;

    public PayrollRunStatus Status { get; set; }
    public PayrollRunSource Source { get; set; }

    public int EmployeeCount { get; set; }

    public decimal TotalGross { get; set; }
    public decimal TotalNet { get; set; }
    public decimal TotalEmployeeEpf { get; set; }
    public decimal TotalEmployerEpf { get; set; }
    public decimal TotalEmployeeSocso { get; set; }
    public decimal TotalEmployerSocso { get; set; }
    public decimal TotalEmployeeEis { get; set; }
    public decimal TotalEmployerEis { get; set; }
    public decimal TotalEmployeeSkbbk { get; set; }
    public decimal TotalPcb { get; set; }
    public decimal TotalCp38 { get; set; }
    public decimal TotalZakat { get; set; }
    public decimal TotalHrdf { get; set; }
    public int EmployeesSubjectToHrdf { get; set; }
    public decimal TotalWagesSubjectToHrdf { get; set; }
    public decimal TotalCostToEmployer { get; set; }

    public DateTime? GeneratedAt { get; set; }

    // ---- Approval trail ----
    // Who proposed this month's pay and who put it live, kept apart.
    public DateTime? SubmittedForApprovalAt { get; set; }
    public string? SubmittedForApprovalById { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public string? SubmittedById { get; set; }

    // Why an approver last sent this run back. Survives the return to DRAFT so
    // the submitter can see what to fix; cleared on the next submission.
    public string? ApprovalRejectionReason { get; set; }

    // True when the run's inputs have changed since the payslips were built, so
    // the figures on screen are behind their sources. Written by every
    // adjustment and claim mutation, cleared by generation — and a submission
    // is refused while it is true.
    public bool IsStale { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// A run plus its payslips.
public class PayrollRunDetailDto
{
    public PayrollRunDto Run { get; set; } = new();
    public List<PayslipDto> Payslips { get; set; } = [];
}

public class PayslipDto
{
    public string Id { get; set; } = string.Empty;
    public string EmployeeProfileId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    public string SnapshotName { get; set; } = string.Empty;
    public string? SnapshotEmployeeNumber { get; set; }
    public string? SnapshotPosition { get; set; }
    public string? SnapshotNationality { get; set; }
    public bool SnapshotIsResident { get; set; }

    public SalaryType SnapshotSalaryType { get; set; }
    public decimal? SnapshotMonthlySalary { get; set; }
    public decimal? SnapshotHourlyRate { get; set; }
    public string? SnapshotEpfRatesJson { get; set; }

    // Three separate day figures on purpose — see the note on the Payslip entity
    // for why the s.60I basis and the proration divisor must not be conflated.
    public int TotalWorkingDays { get; set; }
    public int ProratedDays { get; set; }
    public int ProrationDaysInPeriod { get; set; }
    public decimal ProratedFactor { get; set; }

    public decimal? WorkedHours { get; set; }
    public decimal? ExpectedHours { get; set; }
    public decimal? UnpaidLeaveDays { get; set; }

    public decimal BasicPay { get; set; }
    public decimal ProratedPay { get; set; }
    public decimal OtNormalHours { get; set; }
    public decimal OtRestHours { get; set; }
    public decimal OtPublicHours { get; set; }
    public decimal OtPay { get; set; }
    public decimal TotalAllowances { get; set; }
    public decimal TotalReimbursements { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal TotalBenefitsInKind { get; set; }

    public decimal EpfEmployee { get; set; }
    public decimal EpfEmployer { get; set; }
    public decimal SocsoEmployee { get; set; }
    public decimal SocsoEmployer { get; set; }
    public decimal EisEmployee { get; set; }
    public decimal EisEmployer { get; set; }
    public decimal SkbbkEmployee { get; set; }
    public decimal SkbbkWage { get; set; }
    public decimal Pcb { get; set; }
    public decimal PcbNormal { get; set; }
    public decimal PcbAdditional { get; set; }

    // The LHDN formula decomposition behind Pcb, as stored. Null only on
    // payslips generated before phase 6. See PcbBreakdown.
    public string? PcbCalculationJson { get; set; }
    public decimal Cp38 { get; set; }
    public decimal Zakat { get; set; }
    public decimal Hrdf { get; set; }
    public decimal HrdfWage { get; set; }

    public decimal GrossPay { get; set; }
    public decimal NetPay { get; set; }
    public decimal TotalCostToEmployer { get; set; }

    // Stable codes (MISSING_INCOME_TAX_NUMBER etc.) the admin UI maps to copy.
    // The figures are still correct — these block a filing, not the calc.
    public List<string> StatutoryWarnings { get; set; } = [];

    public List<PayslipLineItemDto> LineItems { get; set; } = [];
}

public class PayslipLineItemDto
{
    public string Id { get; set; } = string.Empty;
    public PayslipLineKind Kind { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public decimal? PcbTaxableAmount { get; set; }
    public string? ClaimId { get; set; }
    public bool SubjectToEpf { get; set; }
    public bool SubjectToSocso { get; set; }
    public bool SubjectToEis { get; set; }
    public bool SubjectToPcb { get; set; }
}

// What a generation actually did. `SkippedEmployees` is not an error list: an
// employee who joined after the period ended or left before it started legally
// does not belong on this run, and saying so beats a silently short payslip
// count.
public class GeneratePayrollRunResultDto
{
    public PayrollRunDetailDto Detail { get; set; } = new();
    public int PayslipCount { get; set; }
    public List<SkippedEmployeeDto> SkippedEmployees { get; set; } = [];
}

public class SkippedEmployeeDto
{
    public string EmployeeProfileId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

// An approver sending a pending run back. The reason is optional but strongly
// wanted — it is the only thing the submitter sees explaining the bounce.
public class RejectPayrollRunDto
{
    [MaxLength(1000)]
    public string? Reason { get; set; }
}
