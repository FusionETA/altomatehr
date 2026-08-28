using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType

namespace AltomateHR.Api.Modules.Employees.Dtos;

// The full per-org employee profile. Used for BOTH read (GET) and write (PUT):
// on read every field is populated; on write the client sends the editable fields
// and the three context fields (Id/Email/Name) are ignored (they come from the
// route + User). Enums serialize as strings (global JsonStringEnumConverter).
public class EmployeeProfileDto
{
    // ---- Context (read-only; server-set) ----
    public string Id { get; set; } = string.Empty;        // the user id
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // ---- Personal / demographic ----
    public string? Phone { get; set; }
    public string? AlternateEmail { get; set; }
    public Gender? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? Nationality { get; set; }
    public string? Race { get; set; }
    public bool HasPr { get; set; }
    public IdType? IdType { get; set; }
    public string? IdNumber { get; set; }
    public MaritalStatus? MaritalStatus { get; set; }
    public bool IsResident { get; set; } = true;
    public bool IsOku { get; set; }
    public string? City { get; set; }
    public string? Postcode { get; set; }
    public string? State { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string? EmergencyContactRelation { get; set; }

    // ---- Employment placement ----
    public DateTime? JoinDate { get; set; }
    public DateTime? LeaveDate { get; set; }
    public string? Department { get; set; }
    public string? Location { get; set; }
    public string? WorkSchedule { get; set; }

    // ---- Spouse / tax relief ----
    public bool? SpouseWorking { get; set; }
    public bool? SpouseDisabled { get; set; }
    public string? SpousePcbNumber { get; set; }
    public string? SpouseIdNumber { get; set; }
    public string? ChildReliefJson { get; set; }

    // ---- Prior-employment YTD ----
    public int? PrevEmploymentYear { get; set; }
    public decimal? PrevRemuneration { get; set; }
    public decimal? PrevEpf { get; set; }
    public decimal? PrevAllowableDeductions { get; set; }
    public decimal? PrevPcb { get; set; }
    public decimal? PrevZakat { get; set; }
    public bool PrevIncludesPriorThisOrgPeriod { get; set; }

    // ---- EPF ----
    public bool ContributeToEpf { get; set; } = true;
    public string? EpfNumber { get; set; }
    public decimal EpfEmployeeRate { get; set; }
    public decimal EpfEmployeeVoluntary { get; set; }
    public decimal EpfEmployerVoluntary { get; set; }

    // ---- SOCSO / EIS / SKBBK ----
    public string? SocsoNumber { get; set; }
    public SocsoScheme? SocsoScheme { get; set; }
    public bool ContributeToEis { get; set; } = true;
    public bool ContributeToSkbbk { get; set; }

    // ---- Income tax ----
    public string? IncomeTaxNumber { get; set; }
    public bool PcbBorneByEmployer { get; set; }
    public string? SsfwNumber { get; set; }
    public bool ReportedToLhdn { get; set; }

    // ---- Bank / payment ----
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BANK_TRANSFER;
    public string? BankName { get; set; }
    public string? BankAccountHolderName { get; set; }
    public string? BankAccountNumber { get; set; }

    // ---- Salary ----
    public SalaryType SalaryType { get; set; } = SalaryType.MONTHLY;
    public decimal? MonthlySalary { get; set; }
    public decimal? HourlyRate { get; set; }
    public string? FixedAllowancesJson { get; set; }

    // ---- Payroll config ----
    public string? PayrollPolicy { get; set; }
    public string? PayrollCycle { get; set; }
    public string? LeaveEntitlementJson { get; set; }
    public string? PayrollDocumentsJson { get; set; }

    // ---- Lifecycle ----
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public string? ArchiveReason { get; set; }
    public DateTime? TemporaryReviewDate { get; set; }
}
