using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using static AltomateHR.Api.Modules.Payroll.PayrollAdjustmentCategories;

namespace AltomateHR.Api.Tests.Payroll;

// The AI split's people rules beyond department (project, job title,
// employment type, role, policy, everyone), its allowance and deduction TYPES,
// and deductions as a source. Globe Engineering's chart is the example: site
// staff to 300/…, HQ to 520/…, directors to their own 520/… accounts.
//
// The invariant is the journal's own: it must balance, and each split must
// add up to exactly the standard lines it replaces.
public class PayrollJournalSplitGroupsTests
{
    private const string Hq = "HQ - SALES & EXPENSES";
    private const string Site = "BANDAR MALAYSIA";

    private static PayrollXeroMapping Mapping() => new()
    {
        TrackingCategoryId = null,
        Accounts = PayrollXeroAccounts.All.ToDictionary(
            slot => slot, slot => (string?)$"acct-{slot}", StringComparer.Ordinal),
    };

    private static PayrollJournal.Input Input(params PayrollJournal.EmployeeRow[] rows) => new()
    {
        PeriodYear = 2026,
        PeriodMonth = 10,
        Mapping = Mapping(),
        AccountCodeById = PayrollXeroAccounts.All.ToDictionary(
            slot => $"acct-{slot}", slot => slot == PayrollXeroAccounts.Salary ? "6100" : $"C-{slot}",
            StringComparer.Ordinal),
        Rows = rows,
    };

    // Net derived from its parts (no statutory deductions), so the standard
    // journal balances.
    private static PayrollJournal.EmployeeRow Person(
        string id,
        decimal pay,
        IReadOnlyList<string>? projects = null,
        string? jobTitle = null,
        string? employmentType = null,
        string? role = null,
        string? policy = null,
        IReadOnlyList<PayrollJournal.JournalLineItem>? allowances = null,
        IReadOnlyList<PayrollJournal.JournalLineItem>? deductions = null)
    {
        allowances ??= [];
        deductions ??= [];
        return new PayrollJournal.EmployeeRow
        {
            EmployeeName = $"Person {id}",
            EmployeeId = id,
            Projects = projects ?? [],
            JobTitle = jobTitle,
            EmploymentType = employmentType,
            Role = role,
            Policy = policy,
            ProratedPay = pay,
            Allowances = allowances,
            Deductions = deductions,
            NetPay = pay + allowances.Sum(a => a.Amount) - deductions.Sum(d => d.Amount),
        };
    }

    private static PayrollJournal.JournalLineItem Item(string category, decimal amount) =>
        new(category, amount, string.Empty);

    private static PayrollJournalSplit.Context Context() => new()
    {
        ActiveAccounts = new[]
        {
            "300/214", "300/201", "520/021", "520/001", "520/006", "520/027", "520/099", "1300",
        }.ToDictionary(c => c, c => $"{c} · account", StringComparer.OrdinalIgnoreCase),
        Projects = new HashSet<string>([Hq, Site], StringComparer.OrdinalIgnoreCase),
        JobTitles = new HashSet<string>(["DIRECTOR", "ENGINEER"], StringComparer.OrdinalIgnoreCase),
        EmploymentTypes = new HashSet<string>(["PERMANENT", "CONTRACT"], StringComparer.OrdinalIgnoreCase),
        Roles = new HashSet<string>(["Employee", "Supervisor"], StringComparer.OrdinalIgnoreCase),
        Policies = new HashSet<string>(["Office hours", "Site shifts"], StringComparer.OrdinalIgnoreCase),
    };

    private static PayrollJournalSplitRule Rule(
        SplitBasis basis, string code, string source = PayrollXeroAccounts.Salary,
        IReadOnlyList<string>? match = null, IReadOnlyList<string>? types = null, string? label = null) => new()
    {
        Source = source,
        Basis = basis,
        AccountCode = code,
        Match = match ?? [],
        Categories = types ?? [],
        Label = label,
    };

    private static PayrollJournalSplit.Result Split(
        PayrollJournal.Input input, params PayrollJournalSplitRule[] rules) =>
        PayrollJournalSplit.Apply(PayrollJournal.Build(input), input.Rows, rules, Context());

    private static decimal Posted(PayrollJournalSplit.Result r, string code) =>
        r.Lines.Where(l => l.AccountCode == code).Sum(l => l.Amount);

    // ─── People ──────────────────────────────────────────────────────────

    [Fact]
    public void By_project_someone_on_HQ_and_a_site_goes_to_the_first_rule_that_names_one_of_theirs()
    {
        var input = Input(
            Person("hq", 5_000m, projects: [Hq]),
            Person("both", 4_000m, projects: [Site, Hq]),
            Person("site", 3_000m, projects: [Site]),
            Person("none", 2_000m));

        var result = Split(input,
            Rule(SplitBasis.PROJECT, "520/021", match: [Hq], label: "Office"),
            Rule(SplitBasis.REMAINDER, "300/214", label: "Project"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(9_000m, Posted(result, "520/021"));   // hq + both
        Assert.Equal(5_000m, Posted(result, "300/214"));   // site + no project
        Assert.Equal(0m, Posted(result, "6100"));
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void A_director_by_job_title_is_taken_before_the_project_rule()
    {
        var input = Input(
            Person("dir", 20_000m, projects: [Hq], jobTitle: "Director"),
            Person("hq", 5_000m, projects: [Hq], jobTitle: "Engineer"),
            Person("site", 3_000m, projects: [Site]));

        var result = Split(input,
            Rule(SplitBasis.JOB_TITLE, "520/006", match: ["DIRECTOR"], label: "Director"),
            Rule(SplitBasis.PROJECT, "520/021", match: [Hq], label: "Office"),
            Rule(SplitBasis.REMAINDER, "300/214", label: "Project"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(20_000m, Posted(result, "520/006"));
        Assert.Equal(5_000m, Posted(result, "520/021"));
        Assert.Equal(3_000m, Posted(result, "300/214"));
    }

    [Fact]
    public void Employment_type_role_and_policy_each_match_their_own_detail()
    {
        var input = Input(
            Person("contract", 1_000m, employmentType: "CONTRACT"),
            Person("supervisor", 2_000m, role: "Supervisor"),
            Person("shifts", 4_000m, policy: "Site shifts"),
            Person("other", 8_000m));

        var result = Split(input,
            Rule(SplitBasis.EMPLOYMENT_TYPE, "520/001", match: ["contract"]),
            Rule(SplitBasis.ROLE, "520/006", match: ["supervisor"]),
            Rule(SplitBasis.POLICY, "300/201", match: ["SITE SHIFTS"]),
            Rule(SplitBasis.REMAINDER, "300/214"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1_000m, Posted(result, "520/001"));
        Assert.Equal(2_000m, Posted(result, "520/006"));
        Assert.Equal(4_000m, Posted(result, "300/201"));
        Assert.Equal(8_000m, Posted(result, "300/214"));
    }

    [Fact]
    public void A_project_that_does_not_exist_refuses_the_split()
    {
        var input = Input(Person("a", 1_000m, projects: [Site]));

        var result = Split(input,
            Rule(SplitBasis.PROJECT, "520/021", match: ["HQ"]),
            Rule(SplitBasis.REMAINDER, "300/214"));

        Assert.False(result.Ok);
        Assert.Contains("no project \"HQ\"", result.Error);
    }

    // ─── Allowance types ─────────────────────────────────────────────────

    [Fact]
    public void A_type_rule_takes_only_that_type_and_the_groups_catch_all_takes_the_rest()
    {
        var input = Input(
            Person("hq", 5_000m, projects: [Hq],
                allowances: [Item(WagesCommission, 800m), Item(AllowanceMeal, 200m)]),
            Person("site", 3_000m, projects: [Site],
                allowances: [Item(WagesCommission, 500m), Item(AllowanceMeal, 100m)]));

        var result = Split(input,
            Rule(SplitBasis.PROJECT, "520/099", PayrollXeroAccounts.Allowance, match: [Hq], types: [WagesCommission]),
            Rule(SplitBasis.PROJECT, "520/001", PayrollXeroAccounts.Allowance, match: [Hq]),
            Rule(SplitBasis.REMAINDER, "300/201", PayrollXeroAccounts.Allowance));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(800m, Posted(result, "520/099"));   // HQ commission only
        Assert.Equal(200m, Posted(result, "520/001"));   // HQ's other allowances
        Assert.Equal(600m, Posted(result, "300/201"));   // all of site's
        Assert.Equal(0m, Posted(result, "C-allowance"));
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void Everyone_picks_out_a_type_for_the_whole_company()
    {
        var input = Input(
            Person("a", 1_000m, allowances: [Item(WagesCommission, 300m), Item(AllowanceMeal, 50m)]),
            Person("b", 1_000m, allowances: [Item(WagesCommission, 200m)]));

        var result = Split(input,
            Rule(SplitBasis.EVERYONE, "520/099", PayrollXeroAccounts.Allowance, types: [WagesCommission]),
            Rule(SplitBasis.REMAINDER, "520/001", PayrollXeroAccounts.Allowance));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(500m, Posted(result, "520/099"));
        Assert.Equal(50m, Posted(result, "520/001"));
    }

    [Fact]
    public void Types_are_refused_on_a_line_without_types_and_on_a_share_rule()
    {
        var input = Input(Person("a", 1_000m, allowances: [Item(WagesCommission, 300m)]));

        var onSalary = Split(input,
            Rule(SplitBasis.EVERYONE, "520/021", types: [WagesCommission]));
        var onRemainder = Split(input,
            Rule(SplitBasis.REMAINDER, "520/001", PayrollXeroAccounts.Allowance, types: [WagesCommission]));
        var unknown = Split(input,
            Rule(SplitBasis.EVERYONE, "520/001", PayrollXeroAccounts.Allowance, types: ["wages_bonus_typo"]),
            Rule(SplitBasis.REMAINDER, "300/201", PayrollXeroAccounts.Allowance));

        Assert.False(onSalary.Ok);
        Assert.Contains("Salary has no types", onSalary.Error);
        Assert.False(onRemainder.Ok);
        Assert.Contains("only a people rule", onRemainder.Error);
        Assert.False(unknown.Ok);
        Assert.Contains("\"wages_bonus_typo\" is not an allowance type", unknown.Error);
    }

    // ─── Deductions ──────────────────────────────────────────────────────

    [Fact]
    public void Deductions_split_by_group_and_type_and_stay_credits()
    {
        var input = Input(
            Person("hq", 5_000m, projects: [Hq],
                deductions: [Item(DeductLoanRepayment, 400m), Item(DeductMiscellaneous, 50m)]),
            Person("site", 3_000m, projects: [Site],
                deductions: [Item(DeductLoanRepayment, 300m)]));

        var result = Split(input,
            Rule(SplitBasis.EVERYONE, "1300", PayrollXeroAccounts.Deduction, types: [DeductLoanRepayment],
                label: "Staff loans"),
            Rule(SplitBasis.REMAINDER, "520/001", PayrollXeroAccounts.Deduction));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(-700m, Posted(result, "1300"));
        Assert.Equal(-50m, Posted(result, "520/001"));
        Assert.Equal(0m, Posted(result, "C-deduction"));   // the standard deduction lines are gone
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void Zakat_and_TP1_are_not_deduction_types_a_rule_can_move()
    {
        var input = Input(Person("a", 1_000m, deductions: [Item(DeductLoanRepayment, 100m)]));

        var result = Split(input,
            Rule(SplitBasis.EVERYONE, "1300", PayrollXeroAccounts.Deduction, types: [DeductZakat, DeductTp1Lifestyle]),
            Rule(SplitBasis.REMAINDER, "520/001", PayrollXeroAccounts.Deduction));

        Assert.False(result.Ok);
        Assert.Contains($"\"{DeductZakat}\" is not a deduction type that posts", result.Error);
        Assert.Contains($"\"{DeductTp1Lifestyle}\" is not a deduction type that posts", result.Error);
    }

    // ─── Who went where ──────────────────────────────────────────────────

    [Fact]
    public void Who_went_where_names_the_rule_and_account_for_each_amount()
    {
        var input = Input(
            Person("both", 4_000m, projects: [Site, Hq], allowances: [Item(WagesCommission, 100m)]),
            Person("site", 3_000m, projects: [Site]));

        var result = Split(input,
            Rule(SplitBasis.PROJECT, "520/021", match: [Hq], label: "Office"),
            Rule(SplitBasis.REMAINDER, "300/214", label: "Project"),
            Rule(SplitBasis.PROJECT, "520/099", PayrollXeroAccounts.Allowance, match: [Hq], types: [WagesCommission]),
            Rule(SplitBasis.REMAINDER, "300/201", PayrollXeroAccounts.Allowance));

        Assert.True(result.Ok, result.Error);
        var both = Assert.Single(result.Assignments!, a => a.EmployeeId == "both");
        Assert.Contains(both.Placements, p =>
            p.Source == PayrollXeroAccounts.Salary && p.AccountCode == "520/021" && p.Amount == 4_000m
            && p.Rule.StartsWith("Office", StringComparison.Ordinal));
        Assert.Contains(both.Placements, p =>
            p.Source == PayrollXeroAccounts.Allowance && p.Category == WagesCommission && p.AccountCode == "520/099");

        var site = Assert.Single(result.Assignments!, a => a.EmployeeId == "site");
        var salary = Assert.Single(site.Placements);
        Assert.Equal("300/214", salary.AccountCode);
        Assert.StartsWith("Project", salary.Rule, StringComparison.Ordinal);
    }
}
