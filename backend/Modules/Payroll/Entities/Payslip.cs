using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Policies.Entities;   // SalaryType
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Payroll.Entities;

// One employee's pay for one run.
//
// Everything a payslip asserts about the employee is SNAPSHOTTED here at
// generation. Raising someone's salary in March must not rewrite what January
// paid them, and a statutory filing has to stay reproducible years later, when
// the profile it came from may have been edited or archived.
//
// `EmployeeProfileId` is kept as a plain string rather than a foreign key for
// the same reason: the payslip outlives the profile.
//
// ⚠️ This carries its own `OrganizationId`. The reference schema keys payslips
// off `employeeProfileId` alone, which would leave the row outside the tenant
// filter — see the tenancy note in the module's CLAUDE.md.
public class Payslip : ITenantScoped
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string OrganizationId { get; set; } = string.Empty;   // tenant

    [MaxLength(40)] public string PayrollRunId { get; set; } = string.Empty;
    [MaxLength(40)] public string EmployeeProfileId { get; set; } = string.Empty;

    // The user the profile belonged to. Kept so the employee portal can find its
    // own payslips (phase 8) without walking a profile that may since be gone.
    [MaxLength(40)] public string UserId { get; set; } = string.Empty;

    // ---- Identity snapshot ----

    [MaxLength(160)] public string SnapshotName { get; set; } = string.Empty;
    [MaxLength(40)] public string? SnapshotEmployeeNumber { get; set; }
    [MaxLength(120)] public string? SnapshotPosition { get; set; }
    [MaxLength(60)] public string? SnapshotNationality { get; set; }
    public bool SnapshotIsResident { get; set; } = true;

    // ---- Salary snapshot ----

    public SalaryType SnapshotSalaryType { get; set; } = SalaryType.MONTHLY;
    [Precision(12, 2)] public decimal? SnapshotMonthlySalary { get; set; }
    [Precision(12, 2)] public decimal? SnapshotHourlyRate { get; set; }

    // The EPF percentages and ringgit split that actually applied, as JSON. The
    // branch overrides the profile's declared rate for everything outside Part
    // A, so storing the profile's number would misdescribe the deduction.
    public string? SnapshotEpfRatesJson { get; set; }

    // ---- Hours and proration ----
    //
    // Two divisors, two statutes, and they must not be read as one pair:
    //
    //   TotalWorkingDays      — s.60I ordinary-rate basis (26, or the calendar
    //                           month, per the org's rule). Drives the hourly
    //                           rate, hence overtime. NOT a proration divisor.
    //   ProratedDays          — calendar days actually employed in the period.
    //   ProrationDaysInPeriod — calendar days in the period.
    //
    // s.18A opens "Notwithstanding section 60I" precisely to override the ÷26
    // basis for an incomplete month, so ProratedFactor is the second pair over
    // itself and never involves TotalWorkingDays. Storing all three keeps the
    // snapshot self-describing: ProratedFactor ≈ ProratedDays ÷
    // ProrationDaysInPeriod, and nothing has to be inferred later.

    public int TotalWorkingDays { get; set; }
    public int ProratedDays { get; set; }
    public int ProrationDaysInPeriod { get; set; }

    [Precision(9, 6)] public decimal ProratedFactor { get; set; } = 1m;

    // Attendance figures, display-only — pay is day-based, not hours-based, for
    // monthly staff. For HOURLY staff WorkedHours IS the paid quantity.
    [Precision(8, 2)] public decimal? WorkedHours { get; set; }
    [Precision(8, 2)] public decimal? ExpectedHours { get; set; }
    [Precision(5, 2)] public decimal? UnpaidLeaveDays { get; set; }

    // ---- Earnings ----

    [Precision(12, 2)] public decimal BasicPay { get; set; }
    [Precision(12, 2)] public decimal ProratedPay { get; set; }

    [Precision(8, 2)] public decimal OtNormalHours { get; set; }
    [Precision(8, 2)] public decimal OtRestHours { get; set; }
    [Precision(8, 2)] public decimal OtPublicHours { get; set; }
    [Precision(12, 2)] public decimal OtPay { get; set; }

    [Precision(12, 2)] public decimal TotalAllowances { get; set; }
    [Precision(12, 2)] public decimal TotalReimbursements { get; set; }
    [Precision(12, 2)] public decimal TotalDeductions { get; set; }

    // Non-cash benefits. Taxable and disclosed on Form EA, but deliberately
    // outside gross and net — the employee never receives the amount.
    [Precision(12, 2)] public decimal TotalBenefitsInKind { get; set; }

    // ---- Statutory ----

    [Precision(12, 2)] public decimal EpfEmployee { get; set; }
    [Precision(12, 2)] public decimal EpfEmployer { get; set; }
    [Precision(12, 2)] public decimal SocsoEmployee { get; set; }
    [Precision(12, 2)] public decimal SocsoEmployer { get; set; }
    [Precision(12, 2)] public decimal EisEmployee { get; set; }
    [Precision(12, 2)] public decimal EisEmployer { get; set; }

    // SKBBK (Skim LINDUNG 24 Jam) — employee-only, from Jun 2026. The wage is
    // persisted separately from the SOCSO wage even though they are equal today,
    // so a historical payslip stays interpretable if PERKESO ever decouples the
    // two wage definitions.
    [Precision(12, 2)] public decimal SkbbkEmployee { get; set; }
    [Precision(12, 2)] public decimal SkbbkWage { get; set; }

    [Precision(12, 2)] public decimal Pcb { get; set; }

    // Court-ordered tax arrears, held apart from Pcb. The MTD spec's X —
    // accumulated PCB paid this year — excludes tax instalments, so folding CP38
    // into Pcb would suppress next month's withholding. It is also remitted
    // through CP39's own CP38 column.
    [Precision(12, 2)] public decimal Cp38 { get; set; }

    [Precision(12, 2)] public decimal Zakat { get; set; }

    [Precision(12, 2)] public decimal Hrdf { get; set; }
    [Precision(12, 2)] public decimal HrdfWage { get; set; }

    // The normal / additional split behind Pcb, for the CP39 file.
    [Precision(12, 2)] public decimal PcbNormal { get; set; }
    [Precision(12, 2)] public decimal PcbAdditional { get; set; }

    // The line-by-line LHDN decomposition (Y, K, P, M, R, B, Z, X…) that
    // produced Pcb, snapshotted so the Detailed Calculations PDF can always show
    // the formula that yielded the amount actually deducted — see PcbBreakdown.
    //
    // Nullable only because payslips generated before phase 6 have none. Every
    // payslip written from now on carries it.
    public string? PcbCalculationJson { get; set; }

    // ---- Aggregates ----

    [Precision(12, 2)] public decimal GrossPay { get; set; }
    [Precision(12, 2)] public decimal NetPay { get; set; }
    [Precision(12, 2)] public decimal TotalCostToEmployer { get; set; }

    // Missing statutory identifiers, as a comma-separated list of stable codes.
    // The calc still runs — the MTD spec never gates it on having a TIN — but
    // the submission files need these, so the admin has to clear them before the
    // run is filed.
    [MaxLength(300)] public string? StatutoryWarnings { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
