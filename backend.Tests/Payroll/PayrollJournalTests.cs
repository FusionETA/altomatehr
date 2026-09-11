using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// The Xero payroll journal.
//
// One invariant dominates: **it must balance**. Xero rejects a journal whose
// debits and credits do not net to zero, and one that somehow posted would
// misstate the company's books. Every scenario below therefore ends by
// checking the balance, and the fixture derives net pay from its own
// components so a test cannot pass by measuring its own arithmetic.
public class PayrollJournalTests
{
    // Every slot mapped to an account id, and every id resolving to a code —
    // the configured-correctly baseline the scenarios vary from.
    private static PayrollXeroMapping Mapping(
        XeroAggregationMode aggregation = XeroAggregationMode.PER_EMPLOYEE,
        XeroLineGroupingMode allowanceMode = XeroLineGroupingMode.UNIFIED,
        XeroLineGroupingMode deductionMode = XeroLineGroupingMode.UNIFIED,
        IReadOnlyDictionary<string, string?>? allowanceAccounts = null,
        IReadOnlyDictionary<string, string?>? deductionAccounts = null,
        string? trackingCategoryId = "trk-1",
        IEnumerable<string>? omitSlots = null)
    {
        var omitted = omitSlots?.ToHashSet(StringComparer.Ordinal) ?? [];

        return new PayrollXeroMapping
        {
            AggregationMode = aggregation,
            TrackingCategoryId = trackingCategoryId,
            AllowanceMode = allowanceMode,
            DeductionMode = deductionMode,
            AllowanceAccounts = allowanceAccounts ?? new Dictionary<string, string?>(),
            DeductionAccounts = deductionAccounts ?? new Dictionary<string, string?>(),
            Accounts = PayrollXeroAccounts.All
                .Where(slot => !omitted.Contains(slot))
                .ToDictionary(slot => slot, slot => (string?)$"acct-{slot}", StringComparer.Ordinal),
        };
    }

    private static Dictionary<string, string> Codes() =>
        PayrollXeroAccounts.All.ToDictionary(
            slot => $"acct-{slot}", slot => $"CODE-{slot}", StringComparer.Ordinal);

    // Net is DERIVED, so the fixture is always internally consistent: a
    // balance test that fed itself its own net would prove nothing.
    private static PayrollJournal.EmployeeRow Row(
        string name = "Aisyah Binti Rahman",
        string project = PayrollJournal.NoProject,
        decimal proratedPay = 5000m,
        decimal otPay = 0m,
        decimal epfEmployee = 550m,
        decimal epfEmployer = 650m,
        decimal socsoEmployee = 24.75m,
        decimal socsoEmployer = 86.65m,
        decimal eisEmployee = 9.90m,
        decimal eisEmployer = 9.90m,
        decimal skbbk = 0m,
        decimal pcb = 110m,
        decimal cp38 = 0m,
        decimal zakat = 0m,
        decimal hrdf = 0m,
        IReadOnlyList<PayrollJournal.JournalLineItem>? allowances = null,
        IReadOnlyList<PayrollJournal.JournalLineItem>? deductions = null,
        IReadOnlyList<PayrollJournal.JournalReimbursement>? reimbursements = null)
    {
        allowances ??= [];
        deductions ??= [];
        reimbursements ??= [];

        // Mirrors PayslipCalculator: benefits in kind and unpaid leave are
        // outside gross, every other allowance and deduction is inside it.
        var cashAllowances = allowances
            .Where(a => PayrollAdjustmentCategories.Find(a.Category)?.NonCash != true)
            .Sum(a => a.Amount);
        var unpaidLeave = deductions
            .Where(d => d.Category == "deduct_unpaid_leave").Sum(d => d.Amount);
        var otherDeductions = deductions
            .Where(d => d.Category != "deduct_unpaid_leave").Sum(d => d.Amount);

        var gross = proratedPay - unpaidLeave + otPay + cashAllowances;
        var net = gross + reimbursements.Sum(r => r.Amount)
            - (epfEmployee + socsoEmployee + eisEmployee + skbbk + pcb + cp38 + zakat + otherDeductions);

        return new PayrollJournal.EmployeeRow
        {
            EmployeeName = name,
            ProjectName = project,
            ProratedPay = proratedPay,
            OtPay = otPay,
            NetPay = net,
            EpfEmployee = epfEmployee,
            EpfEmployer = epfEmployer,
            SocsoEmployee = socsoEmployee,
            SocsoEmployer = socsoEmployer,
            EisEmployee = eisEmployee,
            EisEmployer = eisEmployer,
            SkbbkEmployee = skbbk,
            Pcb = pcb,
            Cp38 = cp38,
            Zakat = zakat,
            Hrdf = hrdf,
            Allowances = allowances,
            Deductions = deductions,
            Reimbursements = reimbursements,
        };
    }

    private static PayrollJournal.Input Input(
        PayrollXeroMapping? mapping = null,
        string? trackingCategoryName = "Project",
        IEnumerable<string>? trackingOptions = null,
        params PayrollJournal.EmployeeRow[] rows) => new()
        {
            PeriodYear = 2026,
            PeriodMonth = 3,
            Mapping = mapping ?? Mapping(),
            AccountCodeById = Codes(),
            TrackingCategoryName = trackingCategoryName,
            TrackingOptions = (trackingOptions ?? [PayrollJournal.NoProject, PayrollJournal.AllProjects])
                .ToHashSet(StringComparer.Ordinal),
            Rows = rows.Length == 0 ? [Row()] : rows,
        };

    private static PayrollJournal.Result Build(PayrollJournal.Input input)
    {
        var result = PayrollJournal.Build(input);
        Assert.True(result.Ok, result.Error);
        return result;
    }

    // ─── The invariant ──────────────────────────────────────────────────

    [Fact]
    public void ASimpleRun_Balances()
    {
        Assert.Equal(0m, Build(Input()).Balance);
    }

    [Fact]
    public void EveryEmployeeShapeStillBalances()
    {
        var result = Build(Input(rows:
        [
            Row(),
            Row("Tan Wei Ming", proratedPay: 8000m, otPay: 450m, hrdf: 80m),
            Row("Arjun Subramaniam", proratedPay: 3200m, pcb: 0m, epfEmployee: 352m),
            Row("Nurul Huda", proratedPay: 12000m, cp38: 200m, zakat: 150m, skbbk: 22.15m),
        ]));

        Assert.Equal(0m, result.Balance);
    }

    // Allowances, deductions, overtime and a reimbursement all at once — the
    // combination is where a missing credit shows up.
    [Fact]
    public void AFullyLoadedPayslipBalances()
    {
        var result = Build(Input(rows:
        [
            Row(
                otPay: 320m,
                allowances:
                [
                    new("allowance_meal", 300m, "Meal"),
                    new("wages_bonus_annual", 5000m, "Annual bonus"),
                ],
                deductions:
                [
                    new("deduct_advance", 200m, "Salary advance"),
                    new("deduct_unpaid_leave", 160m, "2 days unpaid"),
                ],
                reimbursements:
                [
                    new("claim-1", 450m, "Client dinner", "CODE-6100", null),
                ]),
        ]));

        Assert.Equal(0m, result.Balance);
    }

    // A benefit in kind reaches the tax bases but never gross or net. Debiting
    // it would add a debit with no credit and throw the journal out by its
    // whole value.
    [Fact]
    public void ABenefitInKind_IsNotDebited()
    {
        var result = Build(Input(rows:
        [
            Row(allowances: [new("bik_car", 800m, "Company car")]),
        ]));

        Assert.Equal(0m, result.Balance);
        Assert.DoesNotContain(result.Lines, l => l.Description.Contains("Company car"));
    }

    // Unpaid leave is netted out of the salary debit. Crediting it as well
    // would count it twice.
    [Fact]
    public void UnpaidLeave_ShrinksTheSalaryDebitRatherThanBeingCredited()
    {
        var result = Build(Input(rows:
        [
            Row(deductions: [new("deduct_unpaid_leave", 400m, "5 days unpaid")]),
        ]));

        var salary = result.Lines.Single(l => l.Description.StartsWith("SALARY -"));

        Assert.Equal(4600m, salary.Amount);   // 5,000 − 400
        Assert.DoesNotContain(result.Lines, l => l.Description.Contains("unpaid", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(0m, result.Balance);
    }

    // The engine writes overtime as its own column, not a line item, so it is
    // resolved through the `wages_overtime` category.
    [Fact]
    public void Overtime_IsDebitedAsItsOwnLine()
    {
        var result = Build(Input(rows: [Row(otPay: 375m)]));

        var overtime = result.Lines.Single(l => l.Description.StartsWith("OVERTIME -"));

        Assert.Equal(375m, overtime.Amount);
        Assert.Equal(0m, result.Balance);
    }

    // ─── Debits and credits land the right way round ────────────────────

    [Fact]
    public void SalaryAndEmployerContributionsAreDebits()
    {
        var result = Build(Input(rows: [Row(hrdf: 50m)]));

        Assert.All(
            result.Lines.Where(l =>
                l.Description.StartsWith("SALARY -")
                || l.Description.Contains("- EMPLOYER -")),
            l => Assert.True(l.Amount > 0m, $"{l.Description} should be a debit"));
    }

    [Fact]
    public void EveryAccrualIsACredit()
    {
        var result = Build(Input(rows: [Row(hrdf: 50m)]));

        Assert.All(
            result.Lines.Where(l => l.Description.StartsWith("ACCRUAL -")),
            l => Assert.True(l.Amount < 0m, $"{l.Description} should be a credit"));
    }

    // CP38 and zakat are withheld alongside PCB and remitted with it. Leaving
    // them out of the payable puts the journal out by exactly their total.
    [Fact]
    public void Cp38AndZakat_SettleIntoThePcbPayable()
    {
        var result = Build(Input(rows: [Row(pcb: 110m, cp38: 200m, zakat: 150m)]));

        var pcbAccrual = result.Lines.Single(l => l.Description.StartsWith("ACCRUAL - PCB"));

        Assert.Equal(-460m, pcbAccrual.Amount);
        Assert.Equal(0m, result.Balance);
    }

    // SKBBK is a PERKESO scheme collected with SOCSO, so it lands in the same
    // payable rather than needing its own.
    [Fact]
    public void Skbbk_SettlesIntoTheSocsoPayable()
    {
        var result = Build(Input(rows: [Row(skbbk: 22.15m)]));

        var socso = result.Lines.Single(l => l.Description.StartsWith("ACCRUAL - SOCSO"));

        Assert.Equal(-(24.75m + 86.65m + 22.15m), socso.Amount);
        Assert.Equal(0m, result.Balance);
    }

    // ─── Aggregation modes ──────────────────────────────────────────────

    [Fact]
    public void PerEmployee_EmitsOneSalaryLineEach()
    {
        var result = Build(Input(rows:
        [
            Row("Aisyah Binti Rahman"),
            Row("Tan Wei Ming", proratedPay: 6000m),
        ]));

        Assert.Equal(2, result.Lines.Count(l => l.Description.StartsWith("SALARY -")));
        Assert.Contains(result.Lines, l => l.Description == "SALARY - Aisyah Binti Rahman");
    }

    // Collapsing onto the project dimension must not change what the journal
    // says in total — only how it is grouped.
    [Fact]
    public void SumByProject_GroupsWithoutChangingTheTotals()
    {
        PayrollJournal.EmployeeRow[] rows =
        [
            Row("Aisyah Binti Rahman", project: "Bridge"),
            Row("Tan Wei Ming", project: "Bridge", proratedPay: 6000m),
            Row("Arjun Subramaniam", project: "Tunnel", proratedPay: 4000m),
        ];

        var options = new[] { "Bridge", "Tunnel", PayrollJournal.AllProjects };

        var perEmployee = Build(Input(Mapping(), trackingOptions: options, rows: rows));
        var byProject = Build(Input(
            Mapping(XeroAggregationMode.SUM_BY_PROJECT), trackingOptions: options, rows: rows));

        Assert.Equal(0m, byProject.Balance);

        // Two salary lines rather than three — Bridge's two staff share one.
        Assert.Equal(2, byProject.Lines.Count(l => l.Description.StartsWith("SALARY -")));
        Assert.Equal(3, perEmployee.Lines.Count(l => l.Description.StartsWith("SALARY -")));

        Assert.Equal(
            perEmployee.Lines.Where(l => l.Description.StartsWith("SALARY -")).Sum(l => l.Amount),
            byProject.Lines.Where(l => l.Description.StartsWith("SALARY -")).Sum(l => l.Amount));
    }

    // ─── Account resolution ─────────────────────────────────────────────

    // A per-category override wins whatever the mode is, so an admin can stay
    // on UNIFIED and still pin one category to its own account.
    [Fact]
    public void APerCategoryOverride_WinsInUnifiedMode()
    {
        var mapping = Mapping(allowanceAccounts: new Dictionary<string, string?>
        {
            ["allowance_meal"] = "acct-accrualEpf",   // any distinct mapped id
        });

        var result = Build(Input(mapping, rows:
        [
            Row(allowances: [new("allowance_meal", 300m, "Meal")]),
        ]));

        var meal = result.Lines.Single(l => l.Description.Contains("MEAL"));

        Assert.Equal("CODE-accrualEpf", meal.AccountCode);
    }

    // PER_CATEGORY with nothing mapped must refuse rather than quietly
    // dropping the line, which would unbalance the journal.
    [Fact]
    public void PerCategoryWithNoMapping_Refuses()
    {
        var mapping = Mapping(allowanceMode: XeroLineGroupingMode.PER_CATEGORY);

        var result = PayrollJournal.Build(Input(mapping, rows:
        [
            Row(allowances: [new("allowance_meal", 300m, "Meal")]),
        ]));

        Assert.False(result.Ok);
        Assert.Contains("allowance categories with no Xero account", result.Error);
        Assert.Contains("MEAL ALLOWANCE", result.Error);
    }

    [Fact]
    public void NoSalaryAccount_RefusesImmediately()
    {
        var result = PayrollJournal.Build(
            Input(Mapping(omitSlots: [PayrollXeroAccounts.Salary])));

        Assert.False(result.Ok);
        Assert.Contains("Salary", result.Error);
    }

    // An accrual slot the run actually needs is a refusal; one it does not
    // need is not. An org with no HRDF should never be asked to map it.
    [Fact]
    public void AMissingAccrualAccount_RefusesOnlyWhenTheRunNeedsIt()
    {
        var withoutHrdf = PayrollJournal.Build(Input(
            Mapping(omitSlots: [PayrollXeroAccounts.AccrualHrdf]), rows: [Row(hrdf: 0m)]));

        Assert.True(withoutHrdf.Ok, withoutHrdf.Error);

        var withHrdf = PayrollJournal.Build(Input(
            Mapping(omitSlots: [PayrollXeroAccounts.AccrualHrdf]), rows: [Row(hrdf: 80m)]));

        Assert.False(withHrdf.Ok);
        Assert.Contains("accrualHrdf", withHrdf.Error);
    }

    // Every unmapped account is the same class of problem, so one message
    // names them all rather than making the admin discover them one post at
    // a time.
    [Fact]
    public void SeveralUnmappedCategories_AreReportedTogether()
    {
        var mapping = Mapping(
            allowanceMode: XeroLineGroupingMode.PER_CATEGORY,
            deductionMode: XeroLineGroupingMode.PER_CATEGORY);

        var result = PayrollJournal.Build(Input(mapping, rows:
        [
            Row(
                allowances: [new("allowance_meal", 300m, "Meal")],
                deductions: [new("deduct_advance", 200m, "Advance")]),
        ]));

        Assert.False(result.Ok);
        Assert.Contains("allowance categories", result.Error);
        Assert.Contains("deduction categories", result.Error);
    }

    // ─── Reimbursements ─────────────────────────────────────────────────

    [Fact]
    public void AReimbursement_DebitsItsOwnExpenseAccount()
    {
        var result = Build(Input(rows:
        [
            Row(reimbursements: [new("claim-1", 450m, "Client dinner", "CODE-6100", "Bridge")]),
        ]));

        var line = result.Lines.Single(l => l.Description.StartsWith("REIMBURSEMENT -"));

        Assert.Equal("CODE-6100", line.AccountCode);
        Assert.Equal(450m, line.Amount);
        Assert.Equal(0m, result.Balance);
    }

    // Skipping an unmapped claim would leave the net-payable credit — which
    // already includes the reimbursement — without its matching debit.
    [Fact]
    public void AClaimWithNoExpenseAccount_Refuses()
    {
        var result = PayrollJournal.Build(Input(rows:
        [
            Row(reimbursements: [new("claim-1", 450m, "Client dinner", null, null)]),
        ]));

        Assert.False(result.Ok);
        Assert.Contains("Client dinner", result.Error);
    }

    [Fact]
    public void ReimbursementsToTheSameAccountAndProject_AreCombined()
    {
        var result = Build(Input(rows:
        [
            Row(reimbursements:
            [
                new("claim-1", 450m, "Dinner", "CODE-6100", "Bridge"),
                new("claim-2", 120m, "Taxi", "CODE-6100", "Bridge"),
            ]),
        ]));

        var line = result.Lines.Single(l => l.Description.StartsWith("REIMBURSEMENT -"));

        Assert.Equal(570m, line.Amount);
    }

    // ─── Tracking ───────────────────────────────────────────────────────

    // Xero rejects the whole journal for a tracking option it does not
    // recognise, so an unknown project gets no tracking rather than a guess.
    [Fact]
    public void AProjectWithNoMatchingOption_CarriesNoTracking()
    {
        var result = Build(Input(
            trackingOptions: [PayrollJournal.AllProjects],
            rows: [Row(project: "Unknown Site")]));

        var salary = result.Lines.Single(l => l.Description.StartsWith("SALARY -"));

        Assert.Null(salary.TrackingCategoryName);
        Assert.Null(salary.TrackingOption);
    }

    [Fact]
    public void AKnownProject_CarriesTheCategoryAndOption()
    {
        var result = Build(Input(
            trackingOptions: ["Bridge", PayrollJournal.AllProjects],
            rows: [Row(project: "Bridge")]));

        var salary = result.Lines.Single(l => l.Description.StartsWith("SALARY -"));

        Assert.Equal("Project", salary.TrackingCategoryName);
        Assert.Equal("Bridge", salary.TrackingOption);
    }

    [Fact]
    public void NoTrackingCategoryConfigured_MeansNoTrackingAnywhere()
    {
        var result = Build(Input(trackingCategoryName: null));

        Assert.All(result.Lines, l => Assert.Null(l.TrackingCategoryName));
    }

    // Accruals are one liability per agency, so they sit against the
    // all-projects option however the debits are split.
    [Fact]
    public void AccrualsAlwaysSitUnderAllProjects()
    {
        var result = Build(Input(
            trackingOptions: ["Bridge", PayrollJournal.AllProjects],
            rows: [Row(project: "Bridge")]));

        Assert.All(
            result.Lines.Where(l => l.Description.StartsWith("ACCRUAL -")),
            l => Assert.Equal(PayrollJournal.AllProjects, l.TrackingOption));
    }

    // ─── Narration and date ─────────────────────────────────────────────

    // An accountant scanning Xero's journal list identifies a run by this.
    [Fact]
    public void TheNarrationNamesThePeriod()
    {
        Assert.Equal("2026 - SALARY FOR MARCH 2026", Build(Input()).Narration);
    }

    // The journal belongs to the period it pays, not to the day it is posted.
    [Fact]
    public void TheJournalIsDatedTheLastDayOfThePeriod()
    {
        Assert.Equal(new DateTime(2026, 3, 31), Build(Input()).Date);
    }

    [Fact]
    public void ARunWithNoPayslips_Refuses()
    {
        var result = PayrollJournal.Build(new PayrollJournal.Input
        {
            PeriodYear = 2026,
            PeriodMonth = 3,
            Mapping = Mapping(),
            AccountCodeById = Codes(),
            Rows = [],
        });

        Assert.False(result.Ok);
        Assert.Contains("no payslips", result.Error);
    }

    // ─── The balance guard itself ───────────────────────────────────────

    // If the payslips and this arithmetic ever disagree, refusing is the only
    // safe answer — a posted journal that does not balance is a wrong ledger.
    [Fact]
    public void AnInconsistentPayslip_IsRefusedRatherThanPosted()
    {
        // Net pay overstated by RM 500 against its own components.
        var broken = Row() with { NetPay = Row().NetPay + 500m };

        var result = PayrollJournal.Build(Input(rows: [broken]));

        Assert.False(result.Ok);
        Assert.Contains("does not balance", result.Error);
        Assert.Contains("500.00", result.Error);
    }
}
