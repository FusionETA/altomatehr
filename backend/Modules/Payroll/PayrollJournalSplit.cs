using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// The AI split: re-divides the standard journal's debit lines by rules.
//
// Pure, like `PayrollJournal`. The rules were written by AI from the admin's
// prompt, but nothing here is AI — it is arithmetic and checks, so the same
// run and the same rules post the same journal every time.
//
// For each source the rules name (salary, overtime, an employer
// contribution…):
//
//   1. The standard journal's lines for that source are the TOTAL to divide —
//      exactly what would have posted without the split.
//   2. DEPARTMENT / EMPLOYEE rules take their employees' actual amounts, in
//      rule order; an employee is taken by the first rule that matches.
//   3. What is left is the POOL. FIXED rules take RM amounts from it and
//      PERCENT rules take a share of it.
//   4. REMAINDER takes the rest, rounding cents included. Without one, the
//      rules must use the pool up to the sen.
//
// Every check runs and is reported, passed or not, so the settings page can
// show the whole list. Any failure refuses the split, and so the post.
public static class PayrollJournalSplit
{
    public const int MaxTags = 2;

    public sealed record Check(string Label, bool Passed, string? Detail = null);

    public sealed record Result(
        bool Ok,
        IReadOnlyList<PayrollJournal.Line> Lines,
        IReadOnlyList<Check> Checks,
        string? Error)
    {
        public decimal Balance => Lines.Sum(l => l.Amount);
    }

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

        // Department names that exist on employee profiles.
        public IReadOnlySet<string> Departments { get; init; }
            = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // EmployeeProfile id → name.
        public IReadOnlyDictionary<string, string> Employees { get; init; }
            = new Dictionary<string, string>(StringComparer.Ordinal);
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
            ? new Check("Each rule splits a debit line that can be split", true)
            : new Check("Each rule splits a debit line that can be split", false,
                $"Not a splittable debit: {string.Join(", ", unknownSources)}. "
                + "Only expense lines can be split; accruals and deductions always post as set up."));

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

        var departments = rules.Where(r => r.Basis == SplitBasis.DEPARTMENT).SelectMany(r => r.Match).ToList();
        var employees = rules.Where(r => r.Basis == SplitBasis.EMPLOYEE).SelectMany(r => r.Match).ToList();
        if (departments.Count > 0 || employees.Count > 0)
        {
            var unknown = departments.Where(d => !context.Departments.Contains(d))
                .Select(d => $"department \"{d}\"")
                .Concat(employees.Where(e => !context.Employees.ContainsKey(e)).Select(e => $"employee {e}"))
                .Distinct(StringComparer.Ordinal).ToList();
            var empty = rules.Where(r => r.Basis is SplitBasis.DEPARTMENT or SplitBasis.EMPLOYEE && r.Match.Count == 0)
                .Select(Describe).ToList();

            checks.Add(unknown.Count == 0 && empty.Count == 0
                ? new Check("Departments and employees named in the rules exist", true)
                : new Check("Departments and employees named in the rules exist", false,
                    string.Join("; ", unknown.Select(u => $"no {u}")
                        .Concat(empty.Select(e => $"{e} names nobody"))) + "."));
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

    // Apply the rules to a built standard journal.
    public static Result Apply(
        PayrollJournal.Result standard,
        IReadOnlyList<PayrollJournal.EmployeeRow> rows,
        IReadOnlyList<PayrollJournalSplitRule> rules,
        Context context)
    {
        var checks = CheckRules(rules, context).ToList();

        if (!standard.Ok)
        {
            checks.Insert(0, new Check("The standard journal builds", false, standard.Error));
            return Refuse(checks);
        }

        if (checks.Any(c => !c.Passed)) return Refuse(checks);

        var split = new Dictionary<string, List<PayrollJournal.Line>>(StringComparer.Ordinal);
        var problems = new List<string>();
        var summary = new List<string>();

        foreach (var group in rules.GroupBy(r => r.Source, StringComparer.Ordinal))
        {
            var source = group.Key;
            var total = Money.Round2(standard.Lines.Where(l => l.Source == source).Sum(l => l.Amount));

            // Nothing of this kind in the run (no overtime this month, no
            // HRDF): nothing to split, and that is not a failure.
            if (total == 0m) continue;

            var lines = SplitOne(source, total, group.ToList(), rows, context, problems);
            if (lines is null) continue;

            split[source] = lines;
            summary.Add($"{PayrollJournalSplitSources.Label(source)} {total:#,0.00} = {lines.Sum(l => l.Amount):#,0.00}");
        }

        checks.Add(problems.Count == 0
            ? new Check("Each split total equals the standard line it replaces", true,
                summary.Count == 0 ? "Nothing in this run to split." : string.Join("; ", summary))
            : new Check("Each split total equals the standard line it replaces", false,
                string.Join("; ", problems) + "."));

        if (problems.Count > 0) return Refuse(checks);

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
            ? new Result(true, output, checks, null)
            : Refuse(checks);
    }

    private static List<PayrollJournal.Line>? SplitOne(
        string source, decimal total, List<PayrollJournalSplitRule> rules,
        IReadOnlyList<PayrollJournal.EmployeeRow> rows, Context context, List<string> problems)
    {
        var label = PayrollJournalSplitSources.Label(source);
        var lines = new List<PayrollJournal.Line>();

        // 1. Employee rules take their people's actual amounts.
        var taken = new HashSet<int>();
        var takenTotal = 0m;
        foreach (var rule in rules.Where(r => r.Basis is SplitBasis.DEPARTMENT or SplitBasis.EMPLOYEE))
        {
            var amount = 0m;
            for (var i = 0; i < rows.Count; i++)
            {
                if (taken.Contains(i) || !Matches(rule, rows[i])) continue;

                taken.Add(i);
                amount += Money.Round2(AmountFor(source, rows[i]));
            }

            amount = Money.Round2(amount);
            takenTotal += amount;
            if (amount != 0m) lines.Add(LineFor(rule, source, amount, context));
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

        // Fixed amounts and percentages share what the employee rules left.
        // With nothing left (or less than nothing), any share would be paid for
        // by a negative remainder line, which balances but books cost that
        // doesn't exist. Refused whatever the pool's sign.
        if (shares.Count > 0 && pool <= 0m)
        {
            problems.Add($"{label}: department and employee rules take all of it "
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
            if (leftover != 0m) shares.Add(LineFor(remainder, source, leftover, context));
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

        // A zero share (a department with nobody in it this month) posts nothing.
        return [.. lines.Where(l => l.Amount != 0m)];
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
        _ => 0m,
    };

    private static bool Matches(PayrollJournalSplitRule rule, PayrollJournal.EmployeeRow row) => rule.Basis switch
    {
        SplitBasis.DEPARTMENT => row.Department is { } d
            && rule.Match.Any(m => string.Equals(m.Trim(), d.Trim(), StringComparison.OrdinalIgnoreCase)),
        SplitBasis.EMPLOYEE => row.EmployeeId is { } id
            && rule.Match.Contains(id, StringComparer.Ordinal),
        _ => false,
    };

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
            SplitBasis.DEPARTMENT => string.Join(", ", rule.Match),
            _ => "named staff",
        };

        return string.IsNullOrWhiteSpace(rule.Label) ? what : $"{rule.Label.Trim()} ({what})";
    }

    private static Result Refuse(List<Check> checks) =>
        new(false, [], checks,
            "The AI split can't post: " + string.Join(" ", checks.Where(c => !c.Passed)
                .Select(c => c.Detail ?? c.Label)));
}
