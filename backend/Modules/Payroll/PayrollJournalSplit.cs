using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The AI split: re-divides the standard journal's debit lines by rules.
//
// Pure, like `PayrollJournal`. The rules were written by AI from the admin's
// prompt, but nothing here is AI — it is arithmetic and checks, so the same
// run and the same rules post the same journal every time.
//
// For each source the rules name (salary, overtime, allowances, an employer
// contribution, deductions…):
//
//   1. The standard journal's lines for that source are the TOTAL to divide —
//      exactly what would have posted without the split.
//   2. People rules (department, project, job title, employment type, role,
//      policy, named staff, everyone) take their people's actual amounts, in
//      rule order. Allowances and deductions go item by item, so a rule can
//      take only some TYPES (commission, a loan repayment) and leave the rest
//      to the rules after it. Each amount is taken by the first rule that
//      matches it.
//   3. What is left is the POOL. FIXED rules take RM amounts from it and
//      PERCENT rules take a share of it.
//   4. REMAINDER takes the rest, rounding cents included. Without one, the
//      rules must use the pool up to the sen.
//
// Deductions are credits: the split works on their size and posts the lines
// negative, like the standard ones.
//
// Every check runs and is reported, passed or not, so the settings page can
// show the whole list. Any failure refuses the split, and so the post.
public static class PayrollJournalSplit
{
    public const int MaxTags = 2;

    // Warning: passed (posting is not blocked), but worth an admin's eye.
    public sealed record Check(string Label, bool Passed, string? Detail = null, bool Warning = false);

    public sealed record Result(
        bool Ok,
        IReadOnlyList<PayrollJournal.Line> Lines,
        IReadOnlyList<Check> Checks,
        string? Error,
        // Who went where: every person, and which rule took each of their
        // amounts. Shown under the checks so an admin can see the edge cases
        // (someone on two projects) landed where meant before anything posts.
        IReadOnlyList<Assignment>? Assignments = null)
    {
        public decimal Balance => Lines.Sum(l => l.Amount);
    }

    public sealed record Assignment(
        string? EmployeeId, string EmployeeName, IReadOnlyList<Placement> Placements);

    // One amount of one person: what it is (source, and type for allowances
    // and deductions), the rule that took it, and the account it went to.
    // The account is null when it went into the shared pool split by fixed
    // amounts and percentages, which no single account receives.
    public sealed record Placement(
        string Source, string? Category, string Rule, string? AccountCode, decimal Amount);

    // What the rules may refer to, from the org's live data.
    public sealed record Context
    {
        // Active accounts by code, valued "code · name". Archived ones are
        // left out — Xero rejects a line coded to one.
        public IReadOnlyDictionary<string, string> ActiveAccounts { get; init; }
            = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Xero tracking category name → its option names.
        public IReadOnlyDictionary<string, IReadOnlySet<string>> TrackingCategories { get; init; }
            = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);

        // The values each people basis can match, from the org's live data:
        // what is on the profiles, memberships, projects and policies now.
        public IReadOnlySet<string> Departments { get; init; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Live projects with at least one team.
        public IReadOnlySet<string> Projects { get; init; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> JobTitles { get; init; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> EmploymentTypes { get; init; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> Roles { get; init; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public IReadOnlySet<string> Policies { get; init; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // EmployeeProfile id → name.
        public IReadOnlyDictionary<string, string> Employees { get; init; }
            = new Dictionary<string, string>(StringComparer.Ordinal);

        // EmployeeProfile id → their projects (via teams), for the AI's context.
        public IReadOnlyDictionary<string, IReadOnlyList<string>> EmployeeProjects { get; init; }
            = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    }

    // Checks that need no run: do the rules make sense against the org?
    // Run on their own when there is no payroll run to try them on yet.
    public static IReadOnlyList<Check> CheckRules(
        IReadOnlyList<PayrollJournalSplitRule> rules, Context context)
    {
        var checks = new List<Check>();

        if (rules.Count == 0)
        {
            checks.Add(new Check("There are rules to apply", false,
                "No split rules yet. Describe the split and press Generate."));
            return checks;
        }

        var unknownSources = rules.Select(r => r.Source)
            .Where(s => !PayrollJournalSplitSources.All.Contains(s, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).ToList();
        checks.Add(unknownSources.Count == 0
            ? new Check("Each rule splits a line that can be split", true)
            : new Check("Each rule splits a line that can be split", false,
                $"Not a line that can be split: {string.Join(", ", unknownSources)}. "
                + "Costs and deductions can be split; the payables (EPF, SOCSO, EIS, PCB, HRDF, "
                + "net pay) always post as set up."));

        var codes = rules.Select(r => r.AccountCode?.Trim() ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var missingCodes = codes.Where(c => c.Length == 0 || !context.ActiveAccounts.ContainsKey(c)).ToList();
        checks.Add(missingCodes.Count == 0
            ? new Check($"Accounts {string.Join(", ", codes)} exist in the chart of accounts and are active", true)
            : new Check("Every account exists in the chart of accounts and is active", false,
                $"Not found, or archived: {string.Join(", ", missingCodes.Select(c => c.Length == 0 ? "(blank)" : c))}."));

        var tagProblems = new List<string>();
        foreach (var rule in rules)
        {
            if (rule.Tracking.Count > MaxTags)
            {
                tagProblems.Add($"{Describe(rule)} has {rule.Tracking.Count} tags (Xero allows {MaxTags})");
            }

            if (rule.Tracking.Select(t => t.Category).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                < rule.Tracking.Count)
            {
                tagProblems.Add($"{Describe(rule)} uses one tracking category twice");
            }

            foreach (var tag in rule.Tracking)
            {
                if (!context.TrackingCategories.TryGetValue(tag.Category, out var options))
                {
                    tagProblems.Add($"no tracking category \"{tag.Category}\" in Xero");
                }
                else if (!options.Contains(tag.Option))
                {
                    tagProblems.Add($"\"{tag.Option}\" is not an option of {tag.Category}");
                }
            }
        }

        if (rules.Any(r => r.Tracking.Count > 0))
        {
            checks.Add(tagProblems.Count == 0
                ? new Check("Tracking options found in Xero", true)
                : new Check("Tracking options found in Xero", false,
                    string.Join("; ", tagProblems.Distinct(StringComparer.Ordinal)) + "."));
        }

        var people = rules.Where(r => SplitBases.MatchesValues(r.Basis)).ToList();
        if (people.Count > 0)
        {
            var unknown = people
                .SelectMany(r => r.Match.Where(m => !Exists(r.Basis, m, context))
                    .Select(m => r.Basis switch
                    {
                        SplitBasis.EMPLOYEE => $"no employee {m}",
                        SplitBasis.PROJECT => $"no project \"{m}\" with a team",
                        _ => $"no {SplitBases.Label(r.Basis)} \"{m}\"",
                    }))
                .Distinct(StringComparer.Ordinal).ToList();
            var empty = people.Where(r => r.Match.Count == 0).Select(r => $"{Describe(r)} names nobody").ToList();

            checks.Add(unknown.Count == 0 && empty.Count == 0
                ? new Check("The people named in the rules exist", true)
                : new Check("The people named in the rules exist", false,
                    string.Join("; ", unknown.Concat(empty)) + "."));
        }

        var typed = rules.Where(r => r.Categories.Count > 0).ToList();
        if (typed.Count > 0)
        {
            var typeProblems = new List<string>();
            foreach (var rule in typed)
            {
                if (!PayrollJournalSplitSources.HasTypes(rule.Source))
                {
                    typeProblems.Add($"{PayrollJournalSplitSources.Label(rule.Source)} has no types to pick from");
                    continue;
                }

                if (!SplitBases.TakesPeople(rule.Basis))
                {
                    typeProblems.Add($"{Describe(rule)}: only a people rule (or \"everyone\") can pick types");
                }

                typeProblems.AddRange(rule.Categories
                    .Where(c => !IsTypeOf(rule.Source, c))
                    .Select(c => $"\"{c}\" is not {(rule.Source == PayrollXeroAccounts.Deduction ? "a deduction" : "an allowance")} type that posts"));
            }

            checks.Add(typeProblems.Count == 0
                ? new Check("Allowance and deduction types named in the rules exist", true)
                : new Check("Allowance and deduction types named in the rules exist", false,
                    string.Join("; ", typeProblems.Distinct(StringComparer.Ordinal)) + "."));
        }

        var shareProblems = new List<string>();
        foreach (var group in rules.GroupBy(r => r.Source, StringComparer.Ordinal))
        {
            var label = PayrollJournalSplitSources.Label(group.Key);
            var percents = group.Where(r => r.Basis == SplitBasis.PERCENT).ToList();
            var remainders = group.Count(r => r.Basis == SplitBasis.REMAINDER);

            if (percents.Any(r => r.Value is null or <= 0m or > 100m))
                shareProblems.Add($"{label}: a percentage is missing or outside 0–100");
            if (group.Any(r => r.Basis == SplitBasis.FIXED && r.Value is null or <= 0m))
                shareProblems.Add($"{label}: a fixed amount is missing or not above RM 0");

            var percentTotal = percents.Sum(r => r.Value ?? 0m);
            if (percentTotal > 100m)
                shareProblems.Add($"{label}: percentages add up to {percentTotal:0.##}%. Reduce one share or set a remainder line");
            if (remainders > 1)
                shareProblems.Add($"{label}: more than one remainder line");

            var hasFixed = group.Any(r => r.Basis == SplitBasis.FIXED);
            if (remainders == 0 && !hasFixed && percents.Count > 0 && percentTotal != 100m)
                shareProblems.Add($"{label}: percentages add up to {percentTotal:0.##}% with no remainder line");
        }

        checks.Add(shareProblems.Count == 0
            ? new Check("Shares add up: no more than 100%, and the rest has somewhere to go", true)
            : new Check("Shares add up", false, string.Join("; ", shareProblems) + "."));

        return checks;
    }

    // When the org's journal is "Sum by project": the project tracking
    // category and its options, so a split keeps one line per project.
    public sealed record ByProject(string? TrackingCategory, IReadOnlySet<string> TrackingOptions)
    {
        public IReadOnlyList<PayrollJournal.TrackingTag> TagsFor(string project) =>
            !string.IsNullOrWhiteSpace(TrackingCategory) && TrackingOptions.Contains(project)
                ? [new PayrollJournal.TrackingTag(TrackingCategory, project)]
                : [];
    }

    // Apply the rules to a built standard journal. With `byProject`, a people
    // rule (and a remainder that takes only people's own amounts) posts one
    // line per project — each person's amount under their project
    // (EmployeeRow.ProjectName), tagged with its tracking option — instead of
    // one line, as the standard "Sum by project" journal does. A rule that
    // names its own tracking keeps its single tagged line.
    public static Result Apply(
        PayrollJournal.Result standard,
        IReadOnlyList<PayrollJournal.EmployeeRow> rows,
        IReadOnlyList<PayrollJournalSplitRule> rules,
        Context context,
        ByProject? byProject = null)
    {
        var checks = CheckRules(rules, context).ToList();

        if (!standard.Ok)
        {
            checks.Insert(0, new Check("The standard journal builds", false, standard.Error));
            return Refuse(checks);
        }

        if (checks.Any(c => !c.Passed)) return Refuse(checks);

        // People a PROJECT rule took who are in teams under more than one
        // project: their whole pay went to the first rule that matched. Not a
        // failure — flagged so an admin can see who, and change the rules.
        var multiProject = new List<string>();
        foreach (var group in rules.GroupBy(r => r.Source, StringComparer.Ordinal))
        {
            var projectRules = group.Where(r => r.Basis == SplitBasis.PROJECT).ToList();
            if (projectRules.Count == 0) continue;

            // Amount by amount, as `SplitOne` takes them: for allowances and
            // deductions a type rule may take one row and a project rule the
            // next, so "the rule that took them" is decided per row.
            foreach (var unit in UnitsFor(group.Key, rows))
            {
                var row = rows[unit.Row];

                // Only when a PROJECT rule is what took it (not an earlier
                // people or type rule) and the rules name two or more of their
                // projects — otherwise there was no choice to make.
                var first = group.FirstOrDefault(r =>
                    SplitBases.TakesPeople(r.Basis) && Matches(r, row) && TakesType(r, unit.Category));
                if (first?.Basis != SplitBasis.PROJECT) continue;

                var named = row.ProjectsForRules
                    .Where(p => projectRules.Any(r => r.Match.Any(m =>
                        string.Equals(m.Trim(), p.Trim(), StringComparison.OrdinalIgnoreCase))))
                    .ToList();
                if (named.Count < 2) continue;

                multiProject.Add($"{row.EmployeeName} ({string.Join(", ", named)}) → "
                    + $"{(string.IsNullOrWhiteSpace(first.Label) ? string.Join(", ", first.Match) : first.Label.Trim())}");
            }
        }

        multiProject = [.. multiProject.Distinct(StringComparer.Ordinal)];
        if (multiProject.Count > 0)
        {
            checks.Add(new Check(
                "Staff in more than one project",
                true,
                $"No main project is set for them, so their whole pay goes to the first project rule that "
                + $"matches: {string.Join("; ", multiProject)}. Set a main project on their employee profile "
                + "(Employment) to choose.",
                Warning: true));
        }

        var split = new Dictionary<string, List<PayrollJournal.Line>>(StringComparer.Ordinal);
        var problems = new List<string>();
        var summary = new List<string>();
        var placements = new List<(int Row, Placement Placement)>();

        foreach (var group in rules.GroupBy(r => r.Source, StringComparer.Ordinal))
        {
            var source = group.Key;
            var sign = PayrollJournalSplitSources.IsCredit(source) ? -1m : 1m;

            // Worked on the size of the amounts; a credit's lines are negated
            // back at the end.
            var total = sign * Money.Round2(standard.Lines.Where(l => l.Source == source).Sum(l => l.Amount));

            // Nothing of this kind in the run (no overtime this month, no
            // HRDF): nothing to split, and that is not a failure.
            if (total == 0m) continue;

            var lines = SplitOne(source, total, group.ToList(), rows, context, problems, placements, byProject);
            if (lines is null) continue;

            split[source] = [.. lines.Select(l => l with { Amount = sign * l.Amount })];
            summary.Add($"{PayrollJournalSplitSources.Label(source)} {total:#,0.00} = {lines.Sum(l => l.Amount):#,0.00}");
        }

        var assignments = placements
            .GroupBy(p => p.Row)
            .OrderBy(g => rows[g.Key].EmployeeName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Assignment(rows[g.Key].EmployeeId, rows[g.Key].EmployeeName,
                [.. g.Select(p => p.Placement)]))
            .ToList();

        checks.Add(problems.Count == 0
            ? new Check("Each split total equals the standard line it replaces", true,
                summary.Count == 0 ? "Nothing in this run to split." : string.Join("; ", summary))
            : new Check("Each split total equals the standard line it replaces", false,
                string.Join("; ", problems) + "."));

        if (problems.Count > 0) return Refuse(checks, assignments);

        // The split lines go where the first line they replace was, so the
        // journal still reads debits first, accruals last.
        var output = new List<PayrollJournal.Line>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in standard.Lines)
        {
            if (line.Source is { } source && split.TryGetValue(source, out var replacement))
            {
                if (placed.Add(source)) output.AddRange(replacement);
                continue;
            }

            output.Add(line);
        }

        var debits = output.Where(l => l.Amount > 0m).Sum(l => l.Amount);
        var credits = -output.Where(l => l.Amount < 0m).Sum(l => l.Amount);
        var balanced = Math.Abs(debits - credits) <= 0.01m;
        checks.Add(new Check(
            $"Journal balances: debits {debits:#,0.00} = credits {credits:#,0.00}", balanced,
            balanced ? null : $"Out by {debits - credits:0.00}."));

        return balanced
            ? new Result(true, output, checks, null, assignments)
            : Refuse(checks, assignments);
    }

    // One amount of one person: a whole figure (salary, an employer
    // contribution), or one allowance or deduction row with its type.
    private sealed record Unit(int Row, string? Category, decimal Amount);

    private static List<PayrollJournal.Line>? SplitOne(
        string source, decimal total, List<PayrollJournalSplitRule> rules,
        IReadOnlyList<PayrollJournal.EmployeeRow> rows, Context context, List<string> problems,
        List<(int Row, Placement Placement)> placements, ByProject? byProject = null)
    {
        var label = PayrollJournalSplitSources.Label(source);
        var lines = new List<PayrollJournal.Line>();
        var units = UnitsFor(source, rows);

        // 1. People rules take their people's actual amounts.
        var taken = new HashSet<int>();
        var takenTotal = 0m;
        foreach (var rule in rules.Where(r => SplitBases.TakesPeople(r.Basis)))
        {
            var byProjectAmounts = new SortedDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (taken.Contains(i) || !Matches(rule, rows[unit.Row]) || !TakesType(rule, unit.Category)) continue;

                taken.Add(i);
                var project = rows[unit.Row].ProjectName;
                byProjectAmounts[project] = byProjectAmounts.GetValueOrDefault(project) + unit.Amount;
                if (unit.Amount != 0m)
                {
                    placements.Add((unit.Row, new Placement(
                        source, unit.Category, Describe(rule), rule.AccountCode.Trim(), unit.Amount)));
                }
            }

            foreach (var line in PeopleLines(rule, source, byProjectAmounts, context, byProject))
            {
                takenTotal += line.Amount;
                lines.Add(line);
            }
        }

        // 2. The pool is what they left. Fixed amounts and percentages share it.
        var pool = Money.Round2(total - takenTotal);
        var shares = new List<PayrollJournal.Line>();
        foreach (var rule in rules.Where(r => r.Basis == SplitBasis.FIXED))
        {
            shares.Add(LineFor(rule, source, Money.Round2(rule.Value ?? 0m), context));
        }

        foreach (var rule in rules.Where(r => r.Basis == SplitBasis.PERCENT))
        {
            shares.Add(LineFor(rule, source, Money.Round2(pool * (rule.Value ?? 0m) / 100m), context));
        }

        var allocated = shares.Sum(l => l.Amount);
        var leftover = Money.Round2(pool - allocated);
        var remainder = rules.FirstOrDefault(r => r.Basis == SplitBasis.REMAINDER);

        // Who went into the pool — recorded before any refusal below, so a
        // split refused for money "not taken by any rule" still shows whose.
        // The remainder's account when it alone takes the pool; no single
        // account when fixed and percentage rules share it.
        var hasShares = rules.Any(r => r.Basis is SplitBasis.FIXED or SplitBasis.PERCENT);
        for (var i = 0; i < units.Count; i++)
        {
            if (taken.Contains(i) || units[i].Amount == 0m) continue;

            var (rule, account) = hasShares
                ? ("Shared by the fixed and percentage rules", (string?)null)
                : remainder is not null
                    ? (Describe(remainder), remainder.AccountCode.Trim())
                    : ("Not taken by any rule", null);
            placements.Add((units[i].Row, new Placement(source, units[i].Category, rule, account, units[i].Amount)));
        }

        // Fixed amounts and percentages share what the people rules left.
        // With nothing left (or less than nothing), any share would be paid for
        // by a negative remainder line, which balances but books cost that
        // doesn't exist. Refused whatever the pool's sign.
        if (shares.Count > 0 && pool <= 0m)
        {
            problems.Add($"{label}: the people rules take all of it "
                + $"({total:#,0.00}), so fixed amounts and percentages have nothing to share");
            return null;
        }

        if (allocated > pool && shares.Count > 0)
        {
            problems.Add($"{label}: fixed amounts and percentages take {allocated:#,0.00} "
                + $"but only {pool:#,0.00} is left to share");
            return null;
        }

        if (remainder is not null)
        {
            if (leftover != 0m && !hasShares && ProjectLinesFor(remainder, byProject))
            {
                // Only people's own amounts are left: post them under their
                // projects, like a people rule. Any sen between those amounts
                // and the leftover (the standard line's rounding) goes on the
                // biggest line.
                var byProjectAmounts = new SortedDictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < units.Count; i++)
                {
                    if (taken.Contains(i)) continue;
                    var project = rows[units[i].Row].ProjectName;
                    byProjectAmounts[project] = byProjectAmounts.GetValueOrDefault(project) + units[i].Amount;
                }

                var remainderLines = PeopleLines(remainder, source, byProjectAmounts, context, byProject);
                var gap = Money.Round2(leftover - remainderLines.Sum(l => l.Amount));
                if (gap != 0m && remainderLines.Count > 0)
                {
                    var biggest = remainderLines.IndexOf(remainderLines.MaxBy(l => Math.Abs(l.Amount))!);
                    remainderLines[biggest] = remainderLines[biggest] with
                    {
                        Amount = Money.Round2(remainderLines[biggest].Amount + gap),
                    };
                }
                else if (gap != 0m)
                {
                    remainderLines.Add(LineFor(remainder, source, gap, context));
                }

                shares.AddRange(remainderLines);
            }
            else if (leftover != 0m)
            {
                shares.Add(LineFor(remainder, source, leftover, context));
            }
        }
        else if (leftover != 0m)
        {
            // Percentages that add to 100% can still miss by a sen of
            // rounding; that goes on the last share. Anything bigger is money
            // with no rule to take it.
            var target = shares.Count > 0 ? shares : lines;
            if (Math.Abs(leftover) <= 0.05m && target.Count > 0)
            {
                var last = target[^1];
                target[^1] = last with { Amount = Money.Round2(last.Amount + leftover) };
            }
            else
            {
                problems.Add($"{label}: RM {leftover:#,0.00} is not taken by any rule. Add a remainder line");
                return null;
            }
        }

        lines.AddRange(shares);

        var splitTotal = Money.Round2(lines.Sum(l => l.Amount));
        if (splitTotal != total)
        {
            problems.Add($"{label}: the split comes to {splitTotal:#,0.00}, not {total:#,0.00}");
            return null;
        }

        // A zero share (a group with nobody in it this month) posts nothing.
        return [.. lines.Where(l => l.Amount != 0m)];
    }

    // The source's amounts, person by person — and for allowances and
    // deductions, row by row, so a rule can take some types and not others.
    // The same rows, filters and rounding the standard journal posts.
    private static List<Unit> UnitsFor(string source, IReadOnlyList<PayrollJournal.EmployeeRow> rows)
    {
        var units = new List<Unit>();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            switch (source)
            {
                case PayrollXeroAccounts.Allowance:
                    units.AddRange(row.Allowances
                        .Where(a => !PayrollJournal.IsNonCash(a) && a.Amount > 0m)
                        .Select(a => new Unit(r, a.Category, Money.Round2(a.Amount))));
                    break;
                case PayrollXeroAccounts.Deduction:
                    units.AddRange(row.Deductions
                        .Where(d => PayrollJournal.PostsAsDeduction(d) && d.Amount > 0m)
                        .Select(d => new Unit(r, d.Category, Money.Round2(d.Amount))));
                    break;
                default:
                    units.Add(new Unit(r, null, Money.Round2(AmountFor(source, row))));
                    break;
            }
        }

        return units;
    }

    // The same per-employee figures the standard journal debits.
    internal static decimal AmountFor(string source, PayrollJournal.EmployeeRow row) => source switch
    {
        PayrollXeroAccounts.Salary => row.ProratedPay - PayrollJournal.UnpaidLeave(row),
        PayrollJournalSplitSources.Overtime => row.OtPay,
        PayrollXeroAccounts.Allowance => row.Allowances
            .Where(a => !PayrollJournal.IsNonCash(a) && a.Amount > 0m).Sum(a => a.Amount),
        PayrollXeroAccounts.EpfEmployer => row.EpfEmployer,
        PayrollXeroAccounts.SocsoEmployer => row.SocsoEmployer,
        PayrollXeroAccounts.EisEmployer => row.EisEmployer,
        PayrollXeroAccounts.HrdfEmployer => row.Hrdf,
        PayrollXeroAccounts.TaxBorneByEmployer => row.PcbBorneByEmployer,
        PayrollXeroAccounts.Deduction => row.Deductions
            .Where(d => PayrollJournal.PostsAsDeduction(d) && d.Amount > 0m).Sum(d => d.Amount),
        _ => 0m,
    };

    private static bool Matches(PayrollJournalSplitRule rule, PayrollJournal.EmployeeRow row) => rule.Basis switch
    {
        SplitBasis.DEPARTMENT => Is(rule, row.Department),
        SplitBasis.PROJECT => row.ProjectsForRules.Any(p => Is(rule, p)),
        SplitBasis.JOB_TITLE => Is(rule, row.JobTitle),
        SplitBasis.EMPLOYMENT_TYPE => Is(rule, row.EmploymentType),
        SplitBasis.ROLE => Is(rule, row.Role),
        SplitBasis.POLICY => Is(rule, row.Policy),
        SplitBasis.EMPLOYEE => row.EmployeeId is { } id
            && rule.Match.Contains(id, StringComparer.Ordinal),
        SplitBasis.EVERYONE => true,
        _ => false,
    };

    // Names match ignoring case and surrounding spaces, as an admin types them.
    private static bool Is(PayrollJournalSplitRule rule, string? value) =>
        value is { } v && rule.Match.Any(m => string.Equals(m.Trim(), v.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool TakesType(PayrollJournalSplitRule rule, string? category) =>
        rule.Categories.Count == 0
        || (category is not null && rule.Categories.Contains(category, StringComparer.Ordinal));

    private static bool Exists(SplitBasis basis, string value, Context context) => basis switch
    {
        SplitBasis.DEPARTMENT => context.Departments.Contains(value.Trim()),
        SplitBasis.PROJECT => context.Projects.Contains(value.Trim()),
        SplitBasis.JOB_TITLE => context.JobTitles.Contains(value.Trim()),
        SplitBasis.EMPLOYMENT_TYPE => context.EmploymentTypes.Contains(value.Trim()),
        SplitBasis.ROLE => context.Roles.Contains(value.Trim()),
        SplitBasis.POLICY => context.Policies.Contains(value.Trim()),
        SplitBasis.EMPLOYEE => context.Employees.ContainsKey(value),
        _ => true,
    };

    // The adjustment types a rule on this source may pick: the ones whose
    // rows the standard journal posts to it.
    public static IEnumerable<PayrollAdjustmentCategoryMeta> TypesFor(string source) =>
        PayrollAdjustmentCategories.All.Values.Where(m => source switch
        {
            PayrollXeroAccounts.Allowance => m.Kind == PayslipLineKind.ALLOWANCE && !m.NonCash,
            PayrollXeroAccounts.Deduction => m.Kind == PayslipLineKind.DEDUCTION
                && PayrollJournal.PostsAsDeduction(new PayrollJournal.JournalLineItem(m.Code, 1m, string.Empty)),
            _ => false,
        });

    private static bool IsTypeOf(string source, string category) =>
        TypesFor(source).Any(m => string.Equals(m.Code, category, StringComparison.Ordinal));

    private static PayrollJournal.Line LineFor(
        PayrollJournalSplitRule rule, string source, decimal amount, Context context)
    {
        var tags = rule.Tracking
            .Select(t => new PayrollJournal.TrackingTag(
                CanonicalCategory(t.Category, context), t.Option))
            .ToList();

        return new PayrollJournal.Line(
            rule.AccountCode.Trim(),
            amount,
            $"{PayrollJournalSplitSources.Label(source).ToUpperInvariant()} - {Describe(rule)}",
            Source: source,
            Tags: tags);
    }

    // Whether a rule's line is broken down by project: the journal is "Sum by
    // project" and the rule names no tracking of its own.
    private static bool ProjectLinesFor(PayrollJournalSplitRule rule, ByProject? byProject) =>
        byProject is not null && rule.Tracking.Count == 0;

    // A people rule's lines: one per project when the journal is by project,
    // otherwise one line for the lot.
    private static List<PayrollJournal.Line> PeopleLines(
        PayrollJournalSplitRule rule, string source, IReadOnlyDictionary<string, decimal> byProjectAmounts,
        Context context, ByProject? byProject)
    {
        if (!ProjectLinesFor(rule, byProject))
        {
            var amount = Money.Round2(byProjectAmounts.Values.Sum());
            return amount == 0m ? [] : [LineFor(rule, source, amount, context)];
        }

        var lines = new List<PayrollJournal.Line>();
        foreach (var (project, raw) in byProjectAmounts)
        {
            var amount = Money.Round2(raw);
            if (amount == 0m) continue;

            var line = LineFor(rule, source, amount, context);
            lines.Add(line with
            {
                Description = $"{line.Description} - {project}",
                Tags = byProject!.TagsFor(project),
            });
        }
        return lines;
    }

    // Xero matches a tracking category by its exact name.
    private static string CanonicalCategory(string name, Context context) =>
        context.TrackingCategories.Keys.FirstOrDefault(k =>
            string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) ?? name;

    private static string Describe(PayrollJournalSplitRule rule)
    {
        var what = rule.Basis switch
        {
            SplitBasis.PERCENT => $"{rule.Value:0.##}%",
            SplitBasis.FIXED => $"RM {rule.Value:#,0.00}",
            SplitBasis.REMAINDER => "remainder",
            SplitBasis.EVERYONE => "everyone",
            SplitBasis.EMPLOYEE => "named staff",
            _ => Values(rule.Match),
        };

        // "Commission, Bonus" — the types it takes, by their payslip names.
        if (rule.Categories.Count > 0)
        {
            what += " · " + Values([.. rule.Categories.Select(c =>
                PayrollAdjustmentCategories.Find(c)?.Label ?? c)]);
        }

        return string.IsNullOrWhiteSpace(rule.Label) ? what : $"{rule.Label.Trim()} ({what})";
    }

    // A journal line's description is read in Xero; a rule naming twenty
    // projects shouldn't make it a paragraph.
    private static string Values(IReadOnlyList<string> values) =>
        values.Count <= 3
            ? string.Join(", ", values)
            : $"{string.Join(", ", values.Take(3))} +{values.Count - 3} more";

    private static Result Refuse(List<Check> checks, IReadOnlyList<Assignment>? assignments = null) =>
        new(false, [], checks,
            "The AI split can't post: " + string.Join(" ", checks.Where(c => !c.Passed)
                .Select(c => c.Detail ?? c.Label)),
            assignments);
}
