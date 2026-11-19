using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// The AI split re-divides the standard journal's debits by rules. The rules
// come from AI; everything tested here is plain code, and the invariant is
// the same as the journal's: **it must balance**, and each split must add up
// to exactly the line it replaces.
public class PayrollJournalSplitTests
{
    private static PayrollXeroMapping Mapping() => new()
    {
        TrackingCategoryId = null,
        Accounts = PayrollXeroAccounts.All.ToDictionary(
            slot => slot, slot => (string?)$"acct-{slot}", StringComparer.Ordinal),
    };

    private static PayrollJournal.EmployeeRow Row(
        string id, string department, decimal pay, decimal otPay = 0m, decimal epfEmployer = 0m,
        IReadOnlyList<string>? projects = null)
    {
        // Net derived from its parts, so the standard journal balances.
        return new PayrollJournal.EmployeeRow
        {
            EmployeeName = $"Person {id}",
            EmployeeId = id,
            Department = department,
            Projects = projects ?? [],
            ProratedPay = pay,
            OtPay = otPay,
            EpfEmployer = epfEmployer,
            EpfEmployee = 0m,
            NetPay = pay + otPay,
        };
    }

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

    private static PayrollJournalSplit.Context Context() => new()
    {
        ActiveAccounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["6110"] = "6110 · Salaries – Sales",
            ["6120"] = "6120 · Salaries – Operations",
            ["6130"] = "6130 · Salaries – Admin",
        },
        TrackingCategories = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Department"] = new HashSet<string>(["Sales", "Operations", "Admin"], StringComparer.Ordinal),
            ["Events"] = new HashSet<string>(["Expo"], StringComparer.Ordinal),
        },
        Departments = new HashSet<string>(["Sales", "Operations", "Admin"], StringComparer.OrdinalIgnoreCase),
        Projects = new HashSet<string>(["HQ", "Site A"], StringComparer.OrdinalIgnoreCase),
        Employees = new Dictionary<string, string> { ["e1"] = "Person e1", ["e2"] = "Person e2", ["e3"] = "Person e3" },
    };

    private static PayrollJournalSplitRule Rule(
        SplitBasis basis, string code, decimal? value = null, string source = PayrollXeroAccounts.Salary,
        IReadOnlyList<string>? match = null, IReadOnlyList<PayrollJournalSplitTag>? tags = null) => new()
    {
        Source = source,
        Basis = basis,
        Value = value,
        AccountCode = code,
        Match = match ?? [],
        Tracking = tags ?? [],
    };

    private static PayrollJournalSplit.Result Split(
        PayrollJournal.Input input, params PayrollJournalSplitRule[] rules) =>
        PayrollJournalSplit.Apply(PayrollJournal.Build(input), input.Rows, rules, Context());

    private static decimal Debited(PayrollJournalSplit.Result r, string code) =>
        r.Lines.Where(l => l.AccountCode == code).Sum(l => l.Amount);

    [Fact]
    public void Percent_with_remainder_matches_the_mock()
    {
        var input = Input(Row("e1", "Sales", 20_000m), Row("e2", "Operations", 28_500m));

        var result = Split(input,
            Rule(SplitBasis.PERCENT, "6110", 40m),
            Rule(SplitBasis.PERCENT, "6120", 35m),
            Rule(SplitBasis.REMAINDER, "6130"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(19_400m, Debited(result, "6110"));
        Assert.Equal(16_975m, Debited(result, "6120"));
        Assert.Equal(12_125m, Debited(result, "6130"));
        Assert.Equal(0m, Debited(result, "6100"));   // the default salary line is gone
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void Rounding_cents_go_to_the_remainder_line()
    {
        var input = Input(Row("e1", "Sales", 100.01m));

        var result = Split(input,
            Rule(SplitBasis.PERCENT, "6110", 33.33m),
            Rule(SplitBasis.PERCENT, "6120", 33.33m),
            Rule(SplitBasis.REMAINDER, "6130"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(100.01m, Debited(result, "6110") + Debited(result, "6120") + Debited(result, "6130"));
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void Percentages_over_100_are_refused()
    {
        var result = Split(Input(Row("e1", "Sales", 1000m)),
            Rule(SplitBasis.PERCENT, "6110", 70m),
            Rule(SplitBasis.PERCENT, "6120", 40m),
            Rule(SplitBasis.REMAINDER, "6130"));

        Assert.False(result.Ok);
        Assert.Contains(result.Checks, c => !c.Passed && c.Detail!.Contains("110%"));
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Money_left_with_no_remainder_line_is_refused()
    {
        var result = Split(Input(Row("e1", "Sales", 1000m)),
            Rule(SplitBasis.FIXED, "6110", 300m));

        Assert.False(result.Ok);
        Assert.Contains("not taken by any rule", result.Error);
    }

    [Fact]
    public void Fixed_amounts_larger_than_the_pool_are_refused()
    {
        var result = Split(Input(Row("e1", "Sales", 1000m)),
            Rule(SplitBasis.FIXED, "6110", 1500m),
            Rule(SplitBasis.REMAINDER, "6130"));

        Assert.False(result.Ok);
        Assert.Contains("only 1,000.00 is left", result.Error);
    }

    [Fact]
    public void Department_rules_take_their_staffs_actual_pay_and_the_rest_shares_the_remainder()
    {
        var input = Input(
            Row("e1", "Sales", 4000m),
            Row("e2", "Sales", 3000m),
            Row("e3", "Admin", 2500m));

        var result = Split(input,
            Rule(SplitBasis.DEPARTMENT, "6110", match: ["sales"]),   // case doesn't matter
            Rule(SplitBasis.REMAINDER, "6130"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(7000m, Debited(result, "6110"));
        Assert.Equal(2500m, Debited(result, "6130"));
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void Fixed_shares_are_refused_when_department_rules_already_took_everything()
    {
        // Without the guard this "balanced" with a −2,000 remainder line.
        var input = Input(Row("e1", "Sales", 6000m), Row("e2", "Operations", 4000m));

        var result = Split(input,
            Rule(SplitBasis.DEPARTMENT, "6110", match: ["Sales"]),
            Rule(SplitBasis.DEPARTMENT, "6120", match: ["Operations"]),
            Rule(SplitBasis.FIXED, "6130", 2000m),
            Rule(SplitBasis.REMAINDER, "6130"));

        Assert.False(result.Ok);
        Assert.Contains("nothing to share", result.Error);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public void Project_rules_take_the_pay_of_staff_in_that_projects_teams()
    {
        var input = Input(
            Row("e1", "", 4000m, projects: ["HQ"]),
            Row("e2", "", 3000m, projects: ["Site A"]),
            Row("e3", "", 2500m));   // in no project

        var result = Split(input,
            Rule(SplitBasis.PROJECT, "6110", match: ["hq"]),
            Rule(SplitBasis.REMAINDER, "6130"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(4000m, Debited(result, "6110"));
        Assert.Equal(5500m, Debited(result, "6130"));
        Assert.DoesNotContain(result.Checks, c => c.Warning);
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void Someone_in_two_projects_goes_to_the_first_rule_and_is_flagged_not_blocked()
    {
        var input = Input(Row("e1", "", 4000m, projects: ["HQ", "Site A"]), Row("e2", "", 1000m));

        var result = Split(input,
            Rule(SplitBasis.PROJECT, "6110", match: ["HQ"]),
            Rule(SplitBasis.PROJECT, "6120", match: ["Site A"]),
            Rule(SplitBasis.REMAINDER, "6130"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(4000m, Debited(result, "6110"));
        Assert.Equal(0m, Debited(result, "6120"));
        var flag = Assert.Single(result.Checks, c => c.Warning);
        Assert.True(flag.Passed);
        Assert.Contains("Person e1 (HQ, Site A)", flag.Detail);
    }

    [Fact]
    public void An_unknown_project_is_refused()
    {
        var result = Split(Input(Row("e1", "", 1000m, projects: ["HQ"])),
            Rule(SplitBasis.PROJECT, "6110", match: ["Head Office"]),
            Rule(SplitBasis.REMAINDER, "6130"));

        Assert.False(result.Ok);
        Assert.Contains("Head Office", result.Error);
    }

    [Fact]
    public void An_employee_is_taken_by_the_first_rule_that_names_them()
    {
        var input = Input(Row("e1", "Sales", 4000m), Row("e2", "Sales", 3000m));

        var result = Split(input,
            Rule(SplitBasis.EMPLOYEE, "6120", match: ["e1"]),
            Rule(SplitBasis.DEPARTMENT, "6110", match: ["Sales"]));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(4000m, Debited(result, "6120"));
        Assert.Equal(3000m, Debited(result, "6110"));
    }

    [Fact]
    public void Employer_contributions_split_on_their_own_and_other_debits_stay_standard()
    {
        var input = Input(Row("e1", "Sales", 5000m, otPay: 200m, epfEmployer: 650m));

        var result = Split(input,
            Rule(SplitBasis.PERCENT, "6110", 50m, source: PayrollXeroAccounts.EpfEmployer),
            Rule(SplitBasis.REMAINDER, "6120", source: PayrollXeroAccounts.EpfEmployer));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(325m, Debited(result, "6110"));
        Assert.Equal(325m, Debited(result, "6120"));
        Assert.Equal(5000m, Debited(result, "6100"));   // salary untouched
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void Split_lines_carry_up_to_two_tracking_tags()
    {
        var result = Split(Input(Row("e1", "Sales", 1000m)),
            Rule(SplitBasis.REMAINDER, "6110",
                tags: [new("department", "Sales"), new("Events", "Expo")]));

        Assert.True(result.Ok, result.Error);
        var line = Assert.Single(result.Lines, l => l.AccountCode == "6110");
        // The category is written as Xero names it.
        Assert.Equal(["Department=Sales", "Events=Expo"], line.AllTags.Select(t => $"{t.Category}={t.Option}"));
    }

    [Fact]
    public void Unknown_accounts_options_departments_and_too_many_tags_are_all_reported()
    {
        var result = Split(Input(Row("e1", "Sales", 1000m)),
            Rule(SplitBasis.DEPARTMENT, "9999", match: ["Marketing"]),
            Rule(SplitBasis.REMAINDER, "6130",
                tags: [new("Department", "Finance"), new("Events", "Expo"), new("Region", "North")]));

        Assert.False(result.Ok);
        var failed = string.Join(" ", result.Checks.Where(c => !c.Passed).Select(c => c.Detail));
        Assert.Contains("9999", failed);
        Assert.Contains("Marketing", failed);
        Assert.Contains("Finance", failed);
        Assert.Contains("3 tags", failed);
    }

    [Fact]
    public void A_source_with_nothing_in_the_run_is_skipped_not_refused()
    {
        var result = Split(Input(Row("e1", "Sales", 1000m)),
            Rule(SplitBasis.REMAINDER, "6110", source: PayrollJournalSplitSources.Overtime),
            Rule(SplitBasis.REMAINDER, "6120"));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(1000m, Debited(result, "6120"));
    }

    [Fact]
    public void A_standard_journal_that_cannot_build_refuses_the_split_too()
    {
        var input = Input(Row("e1", "Sales", 1000m)) with
        {
            Mapping = Mapping() with { Accounts = new Dictionary<string, string?>() },
        };

        var result = Split(input, Rule(SplitBasis.REMAINDER, "6110"));

        Assert.False(result.Ok);
        Assert.Contains(result.Checks, c => c.Label == "The standard journal builds" && !c.Passed);
    }
}
