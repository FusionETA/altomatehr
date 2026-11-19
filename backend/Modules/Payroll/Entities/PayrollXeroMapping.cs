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

    // Local ChartOfAccount ids (what the settings page saves), keyed by the
    // slot names in PayrollXeroAccounts. The sync also accepts a Xero AccountID.
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

    // ---- AI split (advanced) ----
    //
    // STANDARD posts exactly as the accounts above say. AI_SPLIT posts the
    // same journal, then re-divides the debit lines named in SplitRules.
    // The rules were turned out of SplitPrompt by AI once, when the admin
    // pressed Generate; posting applies them by code and never calls AI.
    public XeroJournalMode JournalMode { get; init; } = XeroJournalMode.STANDARD;

    public string? SplitPrompt { get; init; }

    // The admin's answers to the AI's questions, kept apart from the prompt so
    // the prompt stays editable and within its length. Part of the instruction
    // the rules came from, so they are snapshotted with it.
    public IReadOnlyList<string> SplitAnswers { get; init; } = [];

    public IReadOnlyList<PayrollJournalSplitRule> SplitRules { get; init; } = [];
}

public enum XeroJournalMode
{
    STANDARD,
    AI_SPLIT,
}

// How one rule takes its share of a source's total.
//
// People rules — the actual amounts of the people they match:
//   DEPARTMENT      — department (free text on the payroll profile)
//   PROJECT         — any live project they are on (through its teams); someone
//                     on two named projects goes to the first rule, and is flagged
//   JOB_TITLE       — job title in this company
//   EMPLOYMENT_TYPE — employment type on the profile (PERMANENT, CONTRACT…)
//   ROLE            — Employee or Supervisor
//   POLICY          — the attendance/leave policy that applies to them
//   EMPLOYEE        — named people (EmployeeProfile ids)
//   EVERYONE        — everyone not already taken (to pick out a TYPE for all)
// Share rules — what the people rules left:
//   FIXED           — a fixed RM amount (Value)
//   PERCENT         — Value % of what the people rules left over
//   REMAINDER       — whatever is left after all of the above, rounding cents included
//
// People rules are applied first, in order; each amount is taken by the first
// rule that matches it. FIXED and PERCENT then share what is left.
//
// The AI only CHOOSES a basis and values from what exists in the org; the
// matching itself is code, so the same run posts the same journal every time.
public enum SplitBasis
{
    DEPARTMENT,
    PROJECT,
    EMPLOYEE,
    FIXED,
    PERCENT,
    REMAINDER,
    JOB_TITLE,
    EMPLOYMENT_TYPE,
    ROLE,
    POLICY,
    EVERYONE,
}

public static class SplitBases
{
    // The bases that take people's actual amounts, rather than a share.
    public static bool TakesPeople(SplitBasis basis) =>
        basis is not (SplitBasis.FIXED or SplitBasis.PERCENT or SplitBasis.REMAINDER);

    // The people bases that match on a list of values (all but EVERYONE).
    public static bool MatchesValues(SplitBasis basis) =>
        TakesPeople(basis) && basis != SplitBasis.EVERYONE;

    public static string Label(SplitBasis basis) => basis switch
    {
        SplitBasis.DEPARTMENT => "department",
        SplitBasis.PROJECT => "project",
        SplitBasis.JOB_TITLE => "job title",
        SplitBasis.EMPLOYMENT_TYPE => "employment type",
        SplitBasis.ROLE => "role",
        SplitBasis.POLICY => "policy",
        SplitBasis.EMPLOYEE => "employee",
        _ => basis.ToString().ToLowerInvariant(),
    };
}

public sealed record PayrollJournalSplitRule
{
    // Which line it re-divides: a `PayrollJournalSplitSources` key.
    public string Source { get; init; } = PayrollXeroAccounts.Salary;

    public SplitBasis Basis { get; init; } = SplitBasis.REMAINDER;

    // The values a people rule matches: department / project / job title /
    // employment type / role / policy names, or EmployeeProfile ids (EMPLOYEE).
    public IReadOnlyList<string> Match { get; init; } = [];

    // Allowance and deduction rules only: the adjustment category codes
    // (e.g. "wages_commission") the rule takes. Empty = every type.
    public IReadOnlyList<string> Categories { get; init; } = [];

    // RM for FIXED, a percentage (40 = 40%) for PERCENT.
    public decimal? Value { get; init; }

    // The Xero account CODE the share posts to.
    public string AccountCode { get; init; } = string.Empty;

    // Up to two tracking tags, by category and option NAME (as Xero shows them).
    public IReadOnlyList<PayrollJournalSplitTag> Tracking { get; init; } = [];

    // Short words for the journal description, e.g. "Sales".
    public string? Label { get; init; }
}

public sealed record PayrollJournalSplitTag(string Category, string Option);

// The lines a rule can split: the month's costs, plus the deductions taken
// from pay (a credit — a loan repayment, say, belongs in the staff-loan
// account of whoever's group it is). The payables — EPF, SOCSO, EIS, PCB,
// HRDF, net pay — are never split: each must equal what is paid to KWSP,
// PERKESO, LHDN, HRD Corp and the bank.
public static class PayrollJournalSplitSources
{
    public const string Overtime = "overtime";

    public static readonly IReadOnlyList<string> All =
    [
        PayrollXeroAccounts.Salary,
        Overtime,
        PayrollXeroAccounts.Allowance,
        PayrollXeroAccounts.EpfEmployer,
        PayrollXeroAccounts.SocsoEmployer,
        PayrollXeroAccounts.EisEmployer,
        PayrollXeroAccounts.HrdfEmployer,
        PayrollXeroAccounts.TaxBorneByEmployer,
        PayrollXeroAccounts.Deduction,
    ];

    // The sources whose rules may pick out adjustment types.
    public static bool HasTypes(string source) =>
        source is PayrollXeroAccounts.Allowance or PayrollXeroAccounts.Deduction;

    // Deductions are credits: their lines carry negative amounts.
    public static bool IsCredit(string source) => source == PayrollXeroAccounts.Deduction;

    public static string Label(string source) => source switch
    {
        Overtime => "Overtime",
        PayrollXeroAccounts.Allowance => "Allowances",
        PayrollXeroAccounts.EpfEmployer => "EPF — employer",
        PayrollXeroAccounts.SocsoEmployer => "SOCSO — employer",
        PayrollXeroAccounts.EisEmployer => "EIS — employer",
        PayrollXeroAccounts.HrdfEmployer => "HRD Corp levy",
        _ => PayrollXeroAccounts.Label(source),
    };
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
    // Optional: PCB the company pays for staff. Unset falls back to Salary.
    public const string TaxBorneByEmployer = "taxBorneByEmployer";

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
        EpfEmployer, SocsoEmployer, EisEmployer, HrdfEmployer, TaxBorneByEmployer,
        AccrualEpf, AccrualSocso, AccrualEis, AccrualPcb, AccrualHrdf, AccrualSalary,
    ];

    // What the settings page calls each slot (XERO_EXPENSE_SLOTS /
    // XERO_ACCRUAL_SLOTS in the frontend), so a refusal names the dropdown
    // the admin has to change rather than a JSON key.
    public static string Label(string slot) => slot switch
    {
        Salary => "Salary",
        Allowance => "Allowances",
        Deduction => "Deductions",
        EpfEmployer => "EPF — employer contribution",
        SocsoEmployer => "SOCSO — employer contribution",
        EisEmployer => "EIS — employer contribution",
        HrdfEmployer => "HRD Corp levy",
        TaxBorneByEmployer => "PCB borne by employer",
        AccrualSalary => "Net pay payable",
        AccrualEpf => "EPF payable",
        AccrualSocso => "SOCSO payable",
        AccrualEis => "EIS payable",
        AccrualPcb => "PCB payable",
        AccrualHrdf => "HRD Corp levy payable",
        _ => slot,
    };
}
