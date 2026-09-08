namespace AltomateHR.Api.Modules.LhdnForms;

public class LhdnFormEmployer
{
    public string? EmployerName { get; set; }
    public string? EmployerTin { get; set; }
    public string? FullAddress { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? DeclarantName { get; set; }
    public string? DeclarantPosition { get; set; }
    public string? DeclarantIdNumber { get; set; }
}

public class LhdnFormEmployee
{
    public string Name { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string? JobTitle { get; set; }
    public string? IdNumber { get; set; }
    public string? IdType { get; set; }
    public string? IncomeTaxNumber { get; set; }
    public string? Nationality { get; set; }
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? MaritalStatus { get; set; }
    public string? Phone { get; set; }
    public string? AlternateEmail { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? Postcode { get; set; }
    public string? State { get; set; }
    public DateTime? JoinDate { get; set; }
    public DateTime? LeaveDate { get; set; }
    public bool IsArchived { get; set; }
    public string? ArchiveReason { get; set; }
    public string? SpouseIdNumber { get; set; }
    public string? SpousePcbNumber { get; set; }
    public decimal? MonthlySalary { get; set; }
    public bool PcbBorneByEmployer { get; set; }
    /// Sum of recurring positive cash allowances from the fixed-allowances
    /// JSON column. Null when none are configured.
    public decimal? FixedAllowancesTotal { get; set; }
    /// Count of dependent children with pcbDeduction != NONE.
    public int QualifyingChildren { get; set; }
    /// Annual child-relief amount (RM), per LHDN Public Ruling 5/2019 §7.3.
    public decimal AnnualChildRelief { get; set; }
    public int? PrevEmploymentYear { get; set; }
    public decimal? PrevRemuneration { get; set; }
    public decimal? PrevEpf { get; set; }
}

public class LhdnFormMonthPcb
{
    public int Month { get; set; } // 1..12
    public decimal Mtd { get; set; }
    public decimal Cp38 { get; set; }
    public decimal Zakat { get; set; }
}

public class LhdnFormYtd
{
    public decimal GrossSalary { get; set; }
    public decimal BonusAndCommission { get; set; }
    public decimal TotalBik { get; set; }
    public decimal TotalPcb { get; set; }
    public decimal TotalZakat { get; set; }
    public decimal TotalEpfEmployee { get; set; }
}

public class LhdnFormPayload
{
    public string OrganizationName { get; set; } = string.Empty;
    public LhdnFormEmployer Employer { get; set; } = new();
    public LhdnFormEmployee Employee { get; set; } = new();

    /// 12 entries, January (index 0) .. December (index 11). An entry is null
    /// when there's no submitted payroll run for that employee that month —
    /// PCB 2(II)'s table prints a blank row rather than "0.00", so an admin
    /// doesn't mistake "never ran" for "ran, zero tax".
    ///
    /// This rebuild has no payroll-run engine yet, so every entry is null
    /// today; the array stays 12-wide so the column layout is ready the day
    /// payroll runs land, rather than needing another schema change.
    public LhdnFormMonthPcb?[] PerMonth { get; set; } = new LhdnFormMonthPcb?[12];

    public int Year { get; set; }

    /// Year-to-date sums across submitted payslips. Always zero today, for
    /// the same reason as PerMonth.
    public LhdnFormYtd Ytd { get; set; } = new();

    /// False while there is no payroll-run engine to source PerMonth/Ytd —
    /// drives the disclaimer banner on the four YTD-dependent forms so an
    /// admin can't mistake "no data yet" for "confirmed zero".
    public bool HasPayrollHistory { get; set; }

    public DateTime GeneratedAt { get; set; }
}
