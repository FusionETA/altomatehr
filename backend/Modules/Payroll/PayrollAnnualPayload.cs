using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Everything the annual filings need, loaded once: the employer's identity
// and one aggregated row per employee for the whole year.
//
// The renderers are pure functions over this, which is what makes the
// pipe-delimited CP8D formats testable byte for byte.
public sealed record PayrollAnnualPayload
{
    public required int Year { get; init; }

    public required string OrganizationName { get; init; }

    // Null when the org has never filled Company Info in. Each renderer
    // refuses with its own message naming the field it needed.
    public PayrollCompanyInfo? CompanyInfo { get; init; }

    // The LHDN E-number with its letter and punctuation stripped — LHDN's
    // M/P filenames are built from the digits alone. Empty when unset, which
    // the TXT renderers treat as a refusal.
    public string EmployerNo { get; init; } = string.Empty;

    // One row per employee, ordered by employee code so regenerating a file
    // produces the same bytes.
    public IReadOnlyList<AnnualEmployeeRow> Employees { get; init; } = [];
}

// One employee's whole year, summed across the SUBMITTED runs.
//
// Only submitted months count: a draft is not remuneration that was paid, and
// a year-end filing built on one would be a false return.
public sealed record AnnualEmployeeRow
{
    public required string EmployeeProfileId { get; init; }
    public required string EmployeeName { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string? JobTitle { get; init; }

    // ---- Identity, read live from the profile ----
    public string? IdNumber { get; init; }
    public IdType? IdType { get; init; }
    public string? EpfNumber { get; init; }
    public string? SocsoNumber { get; init; }
    public string? IncomeTaxNumber { get; init; }
    public Gender? Gender { get; init; }
    public MaritalStatus? MaritalStatus { get; init; }

    // Drives the CP8D tax category (1 / 2 / 3).
    public bool? SpouseWorking { get; init; }

    // Children whose relief share is FULL or HALF. A child recorded with NONE
    // is tracked on the profile but not claimed, so it is not "qualifying".
    public int QualifyingChildren { get; init; }
    public decimal AnnualChildRelief { get; init; }

    public bool PcbBorneByEmployer { get; init; }

    // ---- The year's money ----
    public decimal GrossSalary { get; init; }

    // Bonus, commission, fees, arrears, director fee — the line items their
    // category marks as additional remuneration. Reported apart from salary
    // on both Form EA and CP8D.
    public decimal BonusAndCommission { get; init; }

    // Benefits in kind and perquisites. Never part of gross or net, but they
    // ARE part of the employee's reportable income for the year.
    public decimal TotalBik { get; init; }

    public decimal TotalPcb { get; init; }
    public decimal TotalCp38 { get; init; }
    public decimal TotalZakat { get; init; }
    public decimal TotalEpfEmployee { get; init; }
    public decimal TotalSocsoEmployee { get; init; }
    public decimal TotalEisEmployee { get; init; }

    // What the employee declares as employment income: salary plus the
    // additional remuneration plus the non-cash benefits.
    public decimal TotalIncome => GrossSalary + BonusAndCommission + TotalBik;
}
