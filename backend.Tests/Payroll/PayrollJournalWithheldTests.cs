using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using static AltomateHR.Api.Modules.Payroll.PayrollAdjustmentCategories;

namespace AltomateHR.Api.Tests.Payroll;

// Deduction rows that are NOT a deductions-account credit, stored the way a
// real payslip stores them: as a line item AND in the payslip's own field.
//
//   · zakat via salary, CP38, Additional PCB — withheld and remitted with
//     PCB, so they are credited to PCB payable, once
//   · TP1 reliefs, self-paid zakat — lower PCB, but nothing came off the pay,
//     so nothing is credited at all
//
// Before, each of these unbalanced the journal (or, for Additional PCB, sent
// it to the deductions account) and the post was refused.
public class PayrollJournalWithheldTests
{
    private const string Deductions = "C-deduction";
    private const string PcbPayable = "C-accrualPcb";

    private static PayrollJournal.Input Input(PayrollJournal.EmployeeRow row, bool byProject = false) => new()
    {
        PeriodYear = 2026,
        PeriodMonth = 10,
        Mapping = new PayrollXeroMapping
        {
            TrackingCategoryId = null,
            AggregationMode = byProject ? XeroAggregationMode.SUM_BY_PROJECT : XeroAggregationMode.PER_EMPLOYEE,
            Accounts = PayrollXeroAccounts.All.ToDictionary(
                slot => slot, slot => (string?)$"acct-{slot}", StringComparer.Ordinal),
        },
        AccountCodeById = PayrollXeroAccounts.All.ToDictionary(
            slot => $"acct-{slot}", slot => slot == PayrollXeroAccounts.Salary ? "6100" : $"C-{slot}",
            StringComparer.Ordinal),
        Rows = [row],
    };

    // RM 5,000 pay, RM 200 PCB, plus whatever the row adds.
    private static PayrollJournal.EmployeeRow Row(
        IReadOnlyList<PayrollJournal.JournalLineItem> deductions,
        decimal zakat = 0m, decimal cp38 = 0m, decimal voluntaryPcb = 0m, decimal takenFromPay = 0m) => new()
    {
        EmployeeName = "Aisyah",
        EmployeeId = "e1",
        ProratedPay = 5_000m,
        Pcb = 200m,
        Zakat = zakat,
        Cp38 = cp38,
        VoluntaryPcb = voluntaryPcb,
        Deductions = deductions,
        NetPay = 5_000m - 200m - takenFromPay,
    };

    private static PayrollJournal.JournalLineItem Item(string category, decimal amount) =>
        new(category, amount, string.Empty);

    private static decimal Posted(PayrollJournal.Result r, string code) =>
        r.Lines.Where(l => l.AccountCode == code).Sum(l => l.Amount);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Zakat_via_salary_is_credited_to_PCB_payable_once(bool byProject)
    {
        var result = PayrollJournal.Build(Input(
            Row([Item(DeductZakat, 50m)], zakat: 50m, takenFromPay: 50m), byProject));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(-250m, Posted(result, PcbPayable));
        Assert.Equal(0m, Posted(result, Deductions));
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void CP38_is_credited_to_PCB_payable_once()
    {
        var result = PayrollJournal.Build(Input(
            Row([Item(DeductCp38, 120m)], cp38: 120m, takenFromPay: 120m)));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(-320m, Posted(result, PcbPayable));
        Assert.Equal(0m, Posted(result, Deductions));
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void Additional_PCB_goes_to_PCB_payable_not_the_deductions_account()
    {
        var result = PayrollJournal.Build(Input(
            Row([Item(DeductAdditionalPcb, 75m)], voluntaryPcb: 75m, takenFromPay: 75m)));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(-275m, Posted(result, PcbPayable));
        Assert.Equal(0m, Posted(result, Deductions));
        Assert.Equal(0m, result.Balance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_TP1_relief_posts_nothing_because_nothing_came_off_the_pay(bool byProject)
    {
        var result = PayrollJournal.Build(Input(Row([Item(DeductTp1Lifestyle, 2_500m)]), byProject));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(-200m, Posted(result, PcbPayable));
        Assert.Equal(0m, Posted(result, Deductions));
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void Self_paid_zakat_is_in_the_payslips_zakat_but_was_never_withheld()
    {
        // The payslip's Zakat counts every zakat row, the self-paid one too.
        var result = PayrollJournal.Build(Input(
            Row([Item(DeductZakat, 50m), Item(DeductZakatTp1, 80m)], zakat: 130m, takenFromPay: 50m)));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(-250m, Posted(result, PcbPayable));   // PCB + the 50 withheld
        Assert.Equal(0m, Posted(result, Deductions));
        Assert.Equal(0m, result.Balance);
    }

    [Fact]
    public void An_ordinary_deduction_still_credits_the_deductions_account()
    {
        var result = PayrollJournal.Build(Input(
            Row([Item(DeductLoanRepayment, 300m), Item(DeductZakat, 50m)], zakat: 50m, takenFromPay: 350m)));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(-300m, Posted(result, Deductions));
        Assert.Equal(-250m, Posted(result, PcbPayable));
        Assert.Equal(0m, result.Balance);
    }
}
