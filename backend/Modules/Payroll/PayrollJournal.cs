using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Modules.Payroll;

// Turns a payroll run into the lines of a Xero manual journal.
//
// Pure: no EF, no HTTP, no clock. Everything it needs arrives on
// `PayrollJournalInput`, which is what makes the one invariant that matters
// here testable at all — **the journal must balance**. Debits and credits
// netting to anything but zero is rejected by Xero, and a journal that posts
// while unbalanced would misstate the company's books.
//
// The shape of the journal:
//
//   DEBIT   salary, overtime, allowances, employer EPF/SOCSO/EIS/HRDF,
//           and each reimbursed claim against its own expense account
//   CREDIT  the deduction lines, and one accrual per agency plus the net
//           payable to staff
//
// Why those particular credits balance those particular debits:
//   · employee EPF/SOCSO/EIS/PCB are inside net pay already, so the accrual
//     credits for them are matched by the salary debit, not by a second one
//   · reimbursements are paid THROUGH payroll, so they raise net pay — the
//     matching debit is the claim's own expense line
//   · unpaid leave is netted out of the salary debit rather than credited,
//     which is why it is skipped in the deduction loop
//   · benefits in kind never touch gross or net, so they are skipped entirely
public static class PayrollJournal
{
    // A project dimension has to say something even when there is no project.
    public const string AllProjects = "(All projects)";
    public const string NoProject = "(No project)";

    // Positive debits, negative credits — Xero's own convention.
    public sealed record Line(
        string AccountCode,
        decimal Amount,
        string Description,
        string? TrackingCategoryName = null,
        string? TrackingOption = null);

    public sealed record Result(
        bool Ok,
        IReadOnlyList<Line> Lines,
        string Narration,
        DateTime Date,
        // Set when Ok is false. Names what the admin has to fix, never a
        // stack trace — every refusal here is a configuration problem.
        string? Error = null)
    {
        public static Result Refused(string error) =>
            new(false, [], string.Empty, default, error);

        public decimal Balance => Lines.Sum(l => l.Amount);
    }

    public sealed record EmployeeRow
    {
        public required string EmployeeName { get; init; }
        public string ProjectName { get; init; } = NoProject;

        // Prorated, NOT basic. The engine builds gross on the prorated figure,
        // so debiting basic would leave the journal off by the proration for
        // every mid-month joiner and everyone on unpaid leave.
        public decimal ProratedPay { get; init; }
        public decimal OtPay { get; init; }
        public decimal NetPay { get; init; }

        public decimal EpfEmployee { get; init; }
        public decimal EpfEmployer { get; init; }
        public decimal SocsoEmployee { get; init; }
        public decimal SocsoEmployer { get; init; }
        public decimal EisEmployee { get; init; }
        public decimal EisEmployer { get; init; }
        public decimal SkbbkEmployee { get; init; }
        public decimal Pcb { get; init; }
        public decimal Cp38 { get; init; }
        public decimal Zakat { get; init; }
        public decimal Hrdf { get; init; }

        public IReadOnlyList<JournalLineItem> Allowances { get; init; } = [];
        public IReadOnlyList<JournalLineItem> Deductions { get; init; } = [];
        public IReadOnlyList<JournalReimbursement> Reimbursements { get; init; } = [];
    }

    public sealed record JournalLineItem(string? Category, decimal Amount, string Label);

    // A claim reimbursed through payroll. `AccountCode` is the claim's own
    // expense account — null when it has none, which is a refusal.
    public sealed record JournalReimbursement(
        string ClaimId, decimal Amount, string Label, string? AccountCode, string? ProjectName);

    public sealed record Input
    {
        public required int PeriodYear { get; init; }
        public required int PeriodMonth { get; init; }
        public required PayrollXeroMapping Mapping { get; init; }

        // Xero account ID → the CODE a journal line carries. Journals address
        // accounts by code; the mapping stores ids.
        public IReadOnlyDictionary<string, string> AccountCodeById { get; init; }
            = new Dictionary<string, string>();

        // The tracking category's name, and the options that exist on it. A
        // project with no matching option gets no tracking rather than an
        // invented one — Xero rejects an unknown option outright.
        public string? TrackingCategoryName { get; init; }
        public IReadOnlySet<string> TrackingOptions { get; init; } = new HashSet<string>();

        public IReadOnlyList<EmployeeRow> Rows { get; init; } = [];
    }

    public static Result Build(Input input)
    {
        if (input.Rows.Count == 0)
        {
            return Result.Refused("This run has no payslips to post. Generate it first.");
        }

        var lines = new List<Line>();
        var unmappedAllowances = new SortedSet<string>(StringComparer.Ordinal);
        var unmappedDeductions = new SortedSet<string>(StringComparer.Ordinal);
        var unmappedClaims = new List<string>();

        var salaryCode = CodeFor(input, PayrollXeroAccounts.Salary);
        if (salaryCode is null)
        {
            return Result.Refused(
                "No Xero account is mapped for Salary. Set it under Payroll Settings → Xero before posting.");
        }

        if (input.Mapping.AggregationMode == XeroAggregationMode.PER_EMPLOYEE)
        {
            PerEmployeeDebits(input, lines, salaryCode, unmappedAllowances, unmappedDeductions);
        }
        else
        {
            ByProjectDebits(input, lines, salaryCode, unmappedAllowances, unmappedDeductions);
        }

        EmployerContributions(input, lines);
        Reimbursements(input, lines, unmappedClaims);

        // Every unmapped account is the same class of problem, so they are
        // reported together — an admin fixing one at a time through four
        // failed posts is four round trips that one message avoids.
        var refusal = Unmapped(unmappedAllowances, unmappedDeductions, unmappedClaims);
        if (refusal is not null) return Result.Refused(refusal);

        var accrualError = Accruals(input, lines);
        if (accrualError is not null) return Result.Refused(accrualError);

        // The journal must net to zero. A sen of slack is allowed for the
        // rounding each line already carries; anything larger is a real
        // disagreement between the payslips and this arithmetic, and posting
        // it would put a wrong number in the company's ledger.
        var balance = lines.Sum(l => l.Amount);
        if (Math.Abs(balance) > 0.01m)
        {
            return Result.Refused(
                $"The payroll journal does not balance (out by {balance:0.00}). Refusing to post — "
                + "regenerate the run and try again.");
        }

        return new Result(
            true,
            lines,
            $"{input.PeriodYear} - SALARY FOR {MonthName(input.PeriodMonth)} {input.PeriodYear}",
            LastDayOfPeriod(input.PeriodYear, input.PeriodMonth));
    }

    // ─── Debits: one line per employee ──────────────────────────────────

    private static void PerEmployeeDebits(
        Input input, List<Line> lines, string salaryCode,
        SortedSet<string> unmappedAllowances, SortedSet<string> unmappedDeductions)
    {
        foreach (var row in input.Rows)
        {
            // Unpaid leave is netted out of the salary debit rather than
            // credited separately — see the deduction loop below.
            var salary = Money.Round2(Math.Max(0m, row.ProratedPay - UnpaidLeave(row)));
            if (salary > 0m)
            {
                lines.Add(Line_(input, salaryCode, salary,
                    $"SALARY - {row.EmployeeName}", row.ProjectName));
            }

            // The engine writes overtime as its own column, not as a line
            // item, so it is resolved through the `wages_overtime` category —
            // which falls back to the unified allowance account.
            if (row.OtPay > 0m)
            {
                var code = AllowanceCode(input, "wages_overtime", unmappedAllowances);
                if (code is not null)
                {
                    lines.Add(Line_(input, code, Money.Round2(row.OtPay),
                        $"OVERTIME - {row.EmployeeName}", row.ProjectName));
                }
            }

            foreach (var item in row.Allowances)
            {
                if (IsNonCash(item)) continue;

                var code = AllowanceCode(input, item.Category, unmappedAllowances);
                if (code is null) continue;

                lines.Add(Line_(input, code, Money.Round2(item.Amount),
                    Describe(item, row.EmployeeName, "ALLOWANCE"), row.ProjectName));
            }

            foreach (var item in row.Deductions)
            {
                if (IsUnpaidLeave(item)) continue;

                var code = DeductionCode(input, item.Category, unmappedDeductions);
                if (code is null) continue;

                lines.Add(Line_(input, code, -Money.Round2(item.Amount),
                    Describe(item, row.EmployeeName, "DEDUCTION"), row.ProjectName));
            }
        }
    }

    // ─── Debits: collapsed onto the project dimension ───────────────────

    private static void ByProjectDebits(
        Input input, List<Line> lines, string salaryCode,
        SortedSet<string> unmappedAllowances, SortedSet<string> unmappedDeductions)
    {
        var salary = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var overtime = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var allowances = new Dictionary<(string Project, string? Category), decimal>();
        var deductions = new Dictionary<(string Project, string? Category), decimal>();

        foreach (var row in input.Rows)
        {
            Add(salary, row.ProjectName, Math.Max(0m, row.ProratedPay - UnpaidLeave(row)));
            Add(overtime, row.ProjectName, row.OtPay);

            foreach (var item in row.Allowances)
            {
                if (IsNonCash(item)) continue;

                allowances.TryGetValue((row.ProjectName, item.Category), out var running);
                allowances[(row.ProjectName, item.Category)] = running + item.Amount;
            }

            foreach (var item in row.Deductions)
            {
                if (IsUnpaidLeave(item)) continue;

                deductions.TryGetValue((row.ProjectName, item.Category), out var running);
                deductions[(row.ProjectName, item.Category)] = running + item.Amount;
            }
        }

        foreach (var (project, amount) in salary.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (amount <= 0m) continue;
            lines.Add(Line_(input, salaryCode, Money.Round2(amount), $"SALARY - {project}", project));
        }

        foreach (var (project, amount) in overtime.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (amount <= 0m) continue;

            var code = AllowanceCode(input, "wages_overtime", unmappedAllowances);
            if (code is null) continue;

            lines.Add(Line_(input, code, Money.Round2(amount), $"OVERTIME - {project}", project));
        }

        foreach (var ((project, category), amount) in Ordered(allowances))
        {
            if (amount <= 0m) continue;

            var code = AllowanceCode(input, category, unmappedAllowances);
            if (code is null) continue;

            lines.Add(Line_(input, code, Money.Round2(amount),
                $"{CategoryLabel(category, "ALLOWANCE")} - {project}", project));
        }

        foreach (var ((project, category), amount) in Ordered(deductions))
        {
            if (amount <= 0m) continue;

            var code = DeductionCode(input, category, unmappedDeductions);
            if (code is null) continue;

            lines.Add(Line_(input, code, -Money.Round2(amount),
                $"{CategoryLabel(category, "DEDUCTION")} - {project}", project));
        }
    }

    // ─── Employer contributions ─────────────────────────────────────────

    // These are expenses, so they split by project like the salary lines do.
    // Only the accrual credits stay summed.
    private static void EmployerContributions(Input input, List<Line> lines)
    {
        var buckets = new Dictionary<string, (decimal Epf, decimal Socso, decimal Eis, decimal Hrdf)>(
            StringComparer.Ordinal);

        foreach (var row in input.Rows)
        {
            buckets.TryGetValue(row.ProjectName, out var b);
            buckets[row.ProjectName] = (
                b.Epf + row.EpfEmployer,
                b.Socso + row.SocsoEmployer,
                b.Eis + row.EisEmployer,
                b.Hrdf + row.Hrdf);
        }

        foreach (var (project, b) in buckets.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            Contribution(input, lines, PayrollXeroAccounts.EpfEmployer, b.Epf,
                $"EPF CONTRIBUTION - EMPLOYER - {project}", project);
            Contribution(input, lines, PayrollXeroAccounts.SocsoEmployer, b.Socso,
                $"SOCSO CONTRIBUTION - EMPLOYER - {project}", project);
            Contribution(input, lines, PayrollXeroAccounts.EisEmployer, b.Eis,
                $"EIS CONTRIBUTION - EMPLOYER - {project}", project);
            Contribution(input, lines, PayrollXeroAccounts.HrdfEmployer, b.Hrdf,
                $"HRDF - EMPLOYER - {project}", project);
        }
    }

    private static void Contribution(
        Input input, List<Line> lines, string slot, decimal amount, string description, string project)
    {
        if (amount <= 0m) return;

        var code = CodeFor(input, slot);
        if (code is null) return;

        lines.Add(Line_(input, code, Money.Round2(amount), description, project));
    }

    // ─── Reimbursements ─────────────────────────────────────────────────

    // Each reimbursed claim debits its OWN expense account; the matching
    // credit rides on the net-payable accrual, which already includes it.
    // Bucketed by (account, project) so the journal stays readable.
    private static void Reimbursements(Input input, List<Line> lines, List<string> unmapped)
    {
        var buckets = new Dictionary<(string Code, string Project), decimal>();

        foreach (var row in input.Rows)
        {
            foreach (var claim in row.Reimbursements)
            {
                if (string.IsNullOrWhiteSpace(claim.AccountCode))
                {
                    // Skipping it would leave the net-payable credit without
                    // its debit and unbalance the whole journal, so this is a
                    // refusal rather than a dropped line.
                    unmapped.Add(string.IsNullOrWhiteSpace(claim.Label) ? claim.ClaimId : claim.Label);
                    continue;
                }

                var project = string.IsNullOrWhiteSpace(claim.ProjectName)
                    ? row.ProjectName
                    : claim.ProjectName!;

                buckets.TryGetValue((claim.AccountCode!, project), out var running);
                buckets[(claim.AccountCode!, project)] = running + claim.Amount;
            }
        }

        foreach (var ((code, project), amount) in buckets
            .OrderBy(b => b.Key.Code, StringComparer.Ordinal)
            .ThenBy(b => b.Key.Project, StringComparer.Ordinal))
        {
            if (amount <= 0m) continue;
            lines.Add(Line_(input, code, Money.Round2(amount), $"REIMBURSEMENT - {project}", project));
        }
    }

    // ─── Accruals ───────────────────────────────────────────────────────

    // One credit per agency plus the net owed to staff, always summed and
    // always against "(All projects)" — a liability to KWSP is one liability,
    // not one per project.
    private static string? Accruals(Input input, List<Line> lines)
    {
        var totals = new (string Slot, decimal Amount, string Description)[]
        {
            (PayrollXeroAccounts.AccrualEpf,
                input.Rows.Sum(r => r.EpfEmployee + r.EpfEmployer),
                "ACCRUAL - EPF CONTRIBUTION (Total Employer & Employee)"),
            (PayrollXeroAccounts.AccrualSocso,
                // SKBBK is a PERKESO scheme collected alongside SOCSO, so it
                // settles into the same payable.
                input.Rows.Sum(r => r.SocsoEmployee + r.SocsoEmployer + r.SkbbkEmployee),
                "ACCRUAL - SOCSO CONTRIBUTION"),
            (PayrollXeroAccounts.AccrualEis,
                input.Rows.Sum(r => r.EisEmployee + r.EisEmployer),
                "ACCRUAL - EIS CONTRIBUTION"),
            (PayrollXeroAccounts.AccrualPcb,
                // CP38 and zakat are withheld from the employee alongside PCB
                // and remitted with it, so they belong in the same payable —
                // without them the net-pay credit has no matching liability
                // and the journal is out by exactly their total.
                input.Rows.Sum(r => r.Pcb + r.Cp38 + r.Zakat),
                "ACCRUAL - PCB DEDUCTION (Employee only)"),
            (PayrollXeroAccounts.AccrualHrdf,
                input.Rows.Sum(r => r.Hrdf),
                "ACCRUAL - HRDF (HRD Corp levy payable)"),
            (PayrollXeroAccounts.AccrualSalary,
                // Includes reimbursements: they are paid through payroll, so
                // the company owes them to staff alongside salary.
                input.Rows.Sum(r => r.NetPay),
                "ACCRUAL - SALARY"),
        };

        var missing = new List<string>();

        foreach (var (slot, amount, description) in totals)
        {
            if (amount <= 0m) continue;

            var code = CodeFor(input, slot);
            if (code is null)
            {
                missing.Add(slot);
                continue;
            }

            lines.Add(Line_(input, code, -Money.Round2(amount), description, AllProjects));
        }

        return missing.Count == 0
            ? null
            : "These Xero accounts are needed by this run but are not mapped: "
              + string.Join(", ", missing)
              + ". Set them under Payroll Settings → Xero before posting.";
    }

    // ─── Account resolution ─────────────────────────────────────────────

    private static string? CodeFor(Input input, string slot)
    {
        if (!input.Mapping.Accounts.TryGetValue(slot, out var accountId)) return null;
        if (string.IsNullOrWhiteSpace(accountId)) return null;

        return input.AccountCodeById.TryGetValue(accountId, out var code) ? code : null;
    }

    // A per-category override WINS whatever the mode is, so an admin can stay
    // on UNIFIED and still pin one category — overtime, say — to its own
    // account without converting everything.
    private static string? AllowanceCode(Input input, string? category, SortedSet<string> unmapped) =>
        CategoryCode(input, category, input.Mapping.AllowanceAccounts,
            input.Mapping.AllowanceMode, PayrollXeroAccounts.Allowance, unmapped);

    private static string? DeductionCode(Input input, string? category, SortedSet<string> unmapped) =>
        CategoryCode(input, category, input.Mapping.DeductionAccounts,
            input.Mapping.DeductionMode, PayrollXeroAccounts.Deduction, unmapped);

    private static string? CategoryCode(
        Input input,
        string? category,
        IReadOnlyDictionary<string, string?> overrides,
        XeroLineGroupingMode mode,
        string unifiedSlot,
        SortedSet<string> unmapped)
    {
        if (category is not null
            && overrides.TryGetValue(category, out var overrideId)
            && !string.IsNullOrWhiteSpace(overrideId)
            && input.AccountCodeById.TryGetValue(overrideId, out var overrideCode))
        {
            return overrideCode;
        }

        if (mode == XeroLineGroupingMode.PER_CATEGORY)
        {
            unmapped.Add(CategoryLabel(category, "Uncategorised"));
            return null;
        }

        var unified = CodeFor(input, unifiedSlot);
        if (unified is null) unmapped.Add(CategoryLabel(category, "Uncategorised"));

        return unified;
    }

    private static string? Unmapped(
        SortedSet<string> allowances, SortedSet<string> deductions, List<string> claims)
    {
        var problems = new List<string>();

        if (allowances.Count > 0)
        {
            problems.Add("allowance categories with no Xero account: " + string.Join(", ", allowances));
        }

        if (deductions.Count > 0)
        {
            problems.Add("deduction categories with no Xero account: " + string.Join(", ", deductions));
        }

        if (claims.Count > 0)
        {
            problems.Add("reimbursement claims with no Xero-linked expense account: "
                + string.Join(", ", claims));
        }

        return problems.Count == 0
            ? null
            : "This run cannot post to Xero yet — " + string.Join("; ", problems) + ".";
    }

    // ─── Small helpers ──────────────────────────────────────────────────

    private static Line Line_(
        Input input, string code, decimal amount, string description, string project) =>
        new(code, amount, description,
            Tracks(input, project) ? input.TrackingCategoryName : null,
            Tracks(input, project) ? project : null);

    // No category configured, or no matching option on it, means no tracking.
    // Xero rejects the whole journal for an option it does not recognise, so
    // inventing one would cost the month.
    private static bool Tracks(Input input, string project) =>
        !string.IsNullOrWhiteSpace(input.TrackingCategoryName) && input.TrackingOptions.Contains(project);

    // Unpaid leave shrinks the salary debit instead of being credited. It was
    // never earned, so crediting it as a deduction would double-count.
    private static decimal UnpaidLeave(EmployeeRow row) =>
        row.Deductions.Where(IsUnpaidLeave).Sum(d => d.Amount);

    // A benefit in kind is recorded on the payslip as an allowance so it
    // reaches the tax bases, but the employee receives no cash: it never
    // enters gross or net, and its real cost is booked elsewhere (the lease,
    // the rent). Debiting it here would add a debit with no matching credit
    // and unbalance the journal by exactly its value.
    private static bool IsNonCash(JournalLineItem item) =>
        PayrollAdjustmentCategories.Find(item.Category)?.NonCash == true;

    private static bool IsUnpaidLeave(JournalLineItem item) =>
        string.Equals(item.Category, "deduct_unpaid_leave", StringComparison.Ordinal);

    private static string Describe(JournalLineItem item, string employeeName, string fallback)
    {
        var label = CategoryLabel(item.Category, fallback);
        var suffix = string.IsNullOrWhiteSpace(item.Label) ? string.Empty : $" ({item.Label})";

        return $"{label} - {employeeName}{suffix}";
    }

    private static string CategoryLabel(string? category, string fallback) =>
        PayrollAdjustmentCategories.Find(category)?.Label.ToUpperInvariant() ?? fallback;

    private static void Add(Dictionary<string, decimal> map, string key, decimal amount)
    {
        map.TryGetValue(key, out var running);
        map[key] = running + amount;
    }

    private static IEnumerable<KeyValuePair<(string Project, string? Category), decimal>> Ordered(
        Dictionary<(string Project, string? Category), decimal> map) =>
        map.OrderBy(e => e.Key.Project, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Category, StringComparer.Ordinal);

    private static DateTime LastDayOfPeriod(int year, int month) =>
        new DateTime(year, month, 1).AddMonths(1).AddDays(-1);

    private static string MonthName(int month) =>
        System.Globalization.CultureInfo.InvariantCulture.DateTimeFormat
            .GetMonthName(Math.Clamp(month, 1, 12)).ToUpperInvariant();
}
