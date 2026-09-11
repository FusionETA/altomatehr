namespace AltomateHR.Api.Modules.Payroll.Entities;

// How a payroll run lays out its expense (debit) lines on the Xero journal.
//
//   PER_EMPLOYEE   — one line per employee per category. What an accountant
//                    reading the journal usually wants: "SALARY - AISYAH …".
//   SUM_BY_PROJECT — one line per project per category, for orgs whose P&L is
//                    organised by project rather than by head.
//
// Accruals are ALWAYS summed, whichever is chosen — they are one liability per
// agency, not one per person.
public enum XeroAggregationMode
{
    PER_EMPLOYEE,
    SUM_BY_PROJECT,
}

// Whether allowance (or deduction) lines all post to one account or each
// category gets its own.
//
//   UNIFIED      — one account for every allowance. One dropdown to maintain.
//   PER_CATEGORY — each category maps to its own account. A cleaner P&L, and
//                  the sync refuses to post if a category on a payslip is
//                  unmapped rather than guessing an account.
public enum XeroLineGroupingMode
{
    UNIFIED,
    PER_CATEGORY,
}

// The account slots an admin configures. Stored as JSON on
// `PayrollSettings.XeroMappingJson`.
//
// Every field is optional so an admin can configure part of it and come back.
// The sync refuses to fire until each account a given RUN actually needs is
// set — which is narrower than "all of them", because an org with no HRDF
// never needs the HRDF slots.
public sealed record PayrollXeroMapping
{
    // Schema version. Bump it when the shape changes so a stored blob written
    // by an older build can be recognised rather than silently misread.
    public int V { get; init; } = 1;

    public XeroAggregationMode AggregationMode { get; init; } = XeroAggregationMode.PER_EMPLOYEE;

    // The Xero tracking category used for the project dimension. Null means
    // the admin has not picked one and lines carry no tracking.
    public string? TrackingCategoryId { get; init; }

    // Xero account IDs, keyed by the slot names in PayrollXeroAccounts.
    public IReadOnlyDictionary<string, string?> Accounts { get; init; }
        = new Dictionary<string, string?>();

    public XeroLineGroupingMode AllowanceMode { get; init; } = XeroLineGroupingMode.UNIFIED;

    // Per-category allowance accounts, keyed by adjustment category code.
    // Kept even in UNIFIED mode so flipping the toggle does not lose the
    // admin's picks.
    public IReadOnlyDictionary<string, string?> AllowanceAccounts { get; init; }
        = new Dictionary<string, string?>();

    public XeroLineGroupingMode DeductionMode { get; init; } = XeroLineGroupingMode.UNIFIED;

    public IReadOnlyDictionary<string, string?> DeductionAccounts { get; init; }
        = new Dictionary<string, string?>();
}

// The slot names used as keys in `PayrollXeroMapping.Accounts`. Constants
// rather than an enum because they are JSON keys: a renamed enum member would
// silently orphan an admin's saved configuration.
public static class PayrollXeroAccounts
{
    // ---- Expense, debited ----
    public const string Salary = "salary";
    public const string Allowance = "allowance";
    public const string Deduction = "deduction";
    public const string EpfEmployer = "epfEmployer";
    public const string SocsoEmployer = "socsoEmployer";
    public const string EisEmployer = "eisEmployer";
    public const string HrdfEmployer = "hrdfEmployer";

    // ---- Accrual, credited, always summed ----
    public const string AccrualEpf = "accrualEpf";
    public const string AccrualSocso = "accrualSocso";
    public const string AccrualEis = "accrualEis";
    public const string AccrualPcb = "accrualPcb";
    public const string AccrualHrdf = "accrualHrdf";
    public const string AccrualSalary = "accrualSalary";

    public static readonly IReadOnlyList<string> All =
    [
        Salary, Allowance, Deduction,
        EpfEmployer, SocsoEmployer, EisEmployer, HrdfEmployer,
        AccrualEpf, AccrualSocso, AccrualEis, AccrualPcb, AccrualHrdf, AccrualSalary,
    ];
}
