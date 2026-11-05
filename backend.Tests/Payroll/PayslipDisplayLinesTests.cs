using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using C = AltomateHR.Api.Modules.Payroll.PayrollAdjustmentCategories;

namespace AltomateHR.Api.Tests.Payroll;

// What a payslip LISTS: the Overtime table, and every deduction by the name it
// was given rather than a lump called "Other deductions".
public class PayslipDisplayLinesTests
{
    private static Payslip WithOvertime(
        decimal hourly, decimal normalHours, decimal restHours, decimal otPay,
        string customJson = "[]") => new()
        {
            OtHourlyRate = hourly,
            OtRateNormal = 1.5m,
            OtRateRest = 2.0m,
            OtRatePublicHoliday = 3.0m,
            OtNormalHours = normalHours,
            OtRestHours = restHours,
            OtCustomJson = customJson,
            OtPay = otPay,
        };

    private static PayslipLineItem Line(PayslipLineKind kind, string label, decimal amount, string? category) =>
        new() { Kind = kind, Label = label, Amount = amount, Category = category };

    // ─── Overtime ───────────────────────────────────────────────────────

    [Fact]
    public void Overtime_OneRowPerMultiplier_WithTheRateForAnHourAtIt()
    {
        // 54.5 h × 12.98 × 1.5 = 1,061.115; 17 h × 12.98 × 2 = 441.32.
        var rows = PayslipDisplayLines.Overtime(WithOvertime(12.98m, 54.5m, 17m, otPay: 1502.44m));

        Assert.Collection(rows,
            r =>
            {
                Assert.Equal("1.5 TIMES", r.Label);
                Assert.Equal(19.47m, r.Rate);
                Assert.Equal(54.5m, r.Hours);
                Assert.Equal(1061.12m, r.Amount);
            },
            r =>
            {
                Assert.Equal("2.0 TIMES", r.Label);
                Assert.Equal(25.96m, r.Rate);
                Assert.Equal(17m, r.Hours);
                Assert.Equal(441.32m, r.Amount);
            });
    }

    [Fact]
    public void Overtime_ACompanyTypeAtTheSameMultiplier_MergesIntoThatRow()
    {
        var custom = """[{"TypeId":"t1","Name":"Site standby","Hours":3,"Multiplier":1.5,"Amount":45}]""";
        var rows = PayslipDisplayLines.Overtime(WithOvertime(10m, 2m, 0m, otPay: 75m, custom));

        var row = Assert.Single(rows);
        Assert.Equal(1.5m, row.Multiplier);
        Assert.Equal(5m, row.Hours);
        Assert.Equal(75m, row.Amount);
    }

    [Fact]
    public void Overtime_RowsAlwaysAddUpToWhatWasPaid()
    {
        // Each row is RM 0.495 → 0.50 on its own, but the month rounds once:
        // 0.99. The sen comes off a row so the table matches the payslip.
        var p = WithOvertime(0.165m, normalHours: 2m, restHours: 0m, otPay: 0.99m);
        p.OtPublicHours = 1m;

        var rows = PayslipDisplayLines.Overtime(p);

        Assert.Equal(2, rows.Count);
        Assert.Equal(0.99m, rows.Sum(r => r.Amount));
    }

    [Fact]
    public void Overtime_APayslipFromBeforeTheRatesWereKept_HasNoTable()
    {
        var p = WithOvertime(12.98m, 54.5m, 17m, otPay: 1502.44m);
        p.OtRateNormal = null;

        Assert.Empty(PayslipDisplayLines.Overtime(p));
    }

    [Fact]
    public void Overtime_RowsThatDontDescribeThePay_AreNotShown()
    {
        Assert.Empty(PayslipDisplayLines.Overtime(WithOvertime(12.98m, 54.5m, 17m, otPay: 1600m)));
    }

    // ─── Deductions ─────────────────────────────────────────────────────

    [Fact]
    public void Deductions_EachByItsDisplayName_LeavingOutWhatIsShownElsewhere()
    {
        var lines = PayslipDisplayLines.Deductions(
        [
            Line(PayslipLineKind.DEDUCTION, "Loan Repayment", 250m, C.DeductLoanRepayment),
            Line(PayslipLineKind.DEDUCTION, "CP38 Arrears (LHDN Order)", 120m, C.DeductCp38),
            Line(PayslipLineKind.DEDUCTION, "Zakat", 50m, C.DeductZakat),
            Line(PayslipLineKind.DEDUCTION, "Uniform", 35m, C.DeductMiscellaneous),
            // Shown inside the PCB row.
            Line(PayslipLineKind.DEDUCTION, "Additional PCB", 100m, C.DeductAdditionalPcb),
            // Comes off gross — in Earnings.
            Line(PayslipLineKind.DEDUCTION, "Advance Deduction", 300m, C.DeductAdvance),
            // A relief: lowers PCB, takes nothing from pay.
            Line(PayslipLineKind.DEDUCTION, "Internet subscription bill", 80m, C.DeductTp1Lifestyle),
            Line(PayslipLineKind.ALLOWANCE, "Standard Allowance", 200m, C.AllowanceStandard),
        ]);

        Assert.Equal(
            ["Loan Repayment", "CP38 Arrears (LHDN Order)", "Zakat", "Uniform"],
            lines.Select(l => l.Label));

        // TotalDeductions holds everything taken from pay, Additional PCB too.
        var p = new Payslip { TotalDeductions = 555m, VoluntaryPcb = 100m };
        Assert.True(PayslipDisplayLines.DeductionsReconcile(p, lines));
    }

    [Fact]
    public void Deductions_AnImportedPayslipWithNoLines_FallsBackToTheTotal()
    {
        var p = new Payslip { TotalDeductions = 250m };

        Assert.False(PayslipDisplayLines.DeductionsReconcile(p, PayslipDisplayLines.Deductions([])));
    }

    [Fact]
    public void NotInPay_IsBenefitsInKindAndReliefs()
    {
        var lines = PayslipDisplayLines.NotInPay(
        [
            Line(PayslipLineKind.ALLOWANCE, "Company car", 400m, C.BikCar),
            Line(PayslipLineKind.DEDUCTION, "Internet subscription bill", 80m, C.DeductTp1Lifestyle),
            Line(PayslipLineKind.DEDUCTION, "Loan Repayment", 250m, C.DeductLoanRepayment),
        ]);

        Assert.Equal(["Company car", "Internet subscription bill"], lines.Select(l => l.Label));
    }
}
