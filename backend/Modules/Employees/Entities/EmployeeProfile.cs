using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType (reused)
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Employees.Entities;

// The rich per-org employee record — the .NET home of the monolith's PayrollProfile.
// One row per (User, Organization): a person can have different statutory/salary
// details in each org they work for, so this is tenant-scoped. The lean login
// identity stays on User; role/supervisor/job-title stay on OrganizationMembership.
//
// NOTE: statutory/salary fields are pure STORAGE for now — there's no payroll
// calculation engine yet. They exist so the employee record is complete and so the
// eventual payroll module has its inputs; nothing computes off them today.
public class EmployeeProfile : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)]
    public string UserId { get; set; } = string.Empty;           // FK → User (one profile per user per org)

    // ---- Personal / demographic ----
    [MaxLength(40)] public string? Phone { get; set; }
    [MaxLength(120)] public string? AlternateEmail { get; set; }
    public Gender? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    [MaxLength(60)] public string? Nationality { get; set; }
    [MaxLength(60)] public string? Race { get; set; }
    public bool HasPr { get; set; }
    public IdType? IdType { get; set; }
    [MaxLength(40)] public string? IdNumber { get; set; }         // IC / passport number
    public MaritalStatus? MaritalStatus { get; set; }
    public bool IsResident { get; set; } = true;
    public bool IsOku { get; set; }                              // person with disability

    // Home address
    [MaxLength(120)] public string? City { get; set; }
    [MaxLength(20)] public string? Postcode { get; set; }
    [MaxLength(60)] public string? State { get; set; }

    // Emergency contact
    [MaxLength(120)] public string? EmergencyContactName { get; set; }
    [MaxLength(40)] public string? EmergencyContactPhone { get; set; }
    [MaxLength(60)] public string? EmergencyContactRelation { get; set; }

    // ---- Employment placement ----
    public DateTime? JoinDate { get; set; }
    public DateTime? LeaveDate { get; set; }
    [MaxLength(120)] public string? Department { get; set; }
    [MaxLength(120)] public string? Location { get; set; }
    [MaxLength(120)] public string? WorkSchedule { get; set; }

    // ---- Spouse / tax relief ----
    public bool? SpouseWorking { get; set; }
    public bool? SpouseDisabled { get; set; }
    [MaxLength(40)] public string? SpousePcbNumber { get; set; }
    [MaxLength(40)] public string? SpouseIdNumber { get; set; }
    public string? ChildReliefJson { get; set; }                 // JSON (structure owned by payroll)

    // ---- Prior-employment YTD (for tax) ----
    public int? PrevEmploymentYear { get; set; }
    [Precision(12, 2)] public decimal? PrevRemuneration { get; set; }
    [Precision(12, 2)] public decimal? PrevEpf { get; set; }
    [Precision(12, 2)] public decimal? PrevAllowableDeductions { get; set; }
    [Precision(12, 2)] public decimal? PrevPcb { get; set; }
    [Precision(12, 2)] public decimal? PrevZakat { get; set; }
    public bool PrevIncludesPriorThisOrgPeriod { get; set; }

    // ---- EPF ----
    public bool ContributeToEpf { get; set; } = true;
    [MaxLength(40)] public string? EpfNumber { get; set; }
    [Precision(6, 4)] public decimal EpfEmployeeRate { get; set; }
    [Precision(12, 2)] public decimal EpfEmployeeVoluntary { get; set; }
    [Precision(12, 2)] public decimal EpfEmployerVoluntary { get; set; }

    // ---- SOCSO / EIS / SKBBK ----
    [MaxLength(40)] public string? SocsoNumber { get; set; }
    public SocsoScheme? SocsoScheme { get; set; }
    public bool ContributeToEis { get; set; } = true;
    public bool ContributeToSkbbk { get; set; }

    // ---- Income tax ----
    [MaxLength(40)] public string? IncomeTaxNumber { get; set; }
    public bool PcbBorneByEmployer { get; set; }
    [MaxLength(40)] public string? SsfwNumber { get; set; }
    public bool ReportedToLhdn { get; set; }

    // ---- Bank / payment ----
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BANK_TRANSFER;
    [MaxLength(120)] public string? BankName { get; set; }
    [MaxLength(120)] public string? BankAccountHolderName { get; set; }
    [MaxLength(60)] public string? BankAccountNumber { get; set; }

    // ---- Salary ----
    public SalaryType SalaryType { get; set; } = SalaryType.MONTHLY;
    [Precision(12, 2)] public decimal? MonthlySalary { get; set; }
    [Precision(12, 2)] public decimal? HourlyRate { get; set; }
    public string? FixedAllowancesJson { get; set; }             // JSON list (owned by payroll)

    // ---- Payroll config ----
    [MaxLength(80)] public string? PayrollPolicy { get; set; }
    [MaxLength(40)] public string? PayrollCycle { get; set; }
    public string? LeaveEntitlementJson { get; set; }            // JSON (owned by payroll/leave)
    public string? PayrollDocumentsJson { get; set; }            // JSON list of document refs

    // ---- Lifecycle ----
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    [MaxLength(200)] public string? ArchiveReason { get; set; }
    public DateTime? TemporaryReviewDate { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
