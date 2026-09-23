using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;

namespace AltomateHR.Api.Tests.Payroll;

// What the Earnings column prints. An employee who claimed RM 85.50 should
// find that line by name on their payslip, not a lump called "Reimbursements"
// they have to reverse-engineer.
public class PayslipEarningLinesTests
{
    private static PayslipPdfModel Model(
        decimal gross, decimal prorated, decimal ot, params PayslipLineItem[] lines) =>
        new()
        {
            Payslip = new Payslip
            {
                GrossPay = gross,
                ProratedPay = prorated,
                OtPay = ot,
                TotalReimbursements = lines
                    .Where(l => l.Kind == PayslipLineKind.REIMBURSEMENT).Sum(l => l.Amount),
            },
            LineItems = lines,
            OrganizationName = "Globe Engineering Sdn Bhd",
            PeriodLabel = "November 2026",
            IssueDate = new DateTime(2026, 11, 30),
            Ytd = new PayslipYtdSummary { Gross = gross, Net = gross },
        };

    private static PayslipLineItem Line(PayslipLineKind kind, string label, decimal amount, string? category = null) =>
        new() { Kind = kind, Label = label, Amount = amount, Category = category };

    [Fact]
    public void AnApprovedClaim_IsItsOwnRow_NamedAfterTheClaim()
    {
        var model = Model(5097.50m, 5000m, 0m,
            Line(PayslipLineKind.REIMBURSEMENT, "Client lunch", 85.50m),
            Line(PayslipLineKind.REIMBURSEMENT, "Parking at client site", 12.00m));

        var lines = PayslipPdf.BuildEarningLines(model);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Label == "Client lunch" && l.Amount == 85.50m);
        Assert.Contains(lines, l => l.Label == "Parking at client site" && l.Amount == 12.00m);
        Assert.True(PayslipPdf.EarningsReconcile(model.Payslip, lines));
    }

    [Fact]
    public void AGrossReducingDeduction_IsNegativeOnTheEarningsSide()
    {
        var model = Model(4800m, 5000m, 0m,
            Line(PayslipLineKind.DEDUCTION, "Unpaid Leave deduction", 200m,
                PayrollAdjustmentCategories.DeductUnpaidLeave));

        var lines = PayslipPdf.BuildEarningLines(model);

        Assert.Equal(-200m, Assert.Single(lines).Amount);
        Assert.True(PayslipPdf.EarningsReconcile(model.Payslip, lines));
    }

    [Fact]
    public void ABenefitInKind_StaysOutOfTheEarningsColumn()
    {
        // Non-cash: it never enters gross and has its own section lower down.
        var bik = PayrollAdjustmentCategories.All.Values
            .First(m => m.NonCash && m.Kind == PayslipLineKind.ALLOWANCE);

        var model = Model(5000m, 5000m, 0m, Line(PayslipLineKind.ALLOWANCE, bik.Label, 300m, bik.Code));

        Assert.Empty(PayslipPdf.BuildEarningLines(model));
    }

    [Fact]
    public void AnImportedPayslip_CarryingTotalsWithNoLineItems_DoesNotReconcile()
    {
        // A YTD import stores aggregates without one line item per column.
        // Itemising those would print rows that visibly don't add up to the
        // "Total earnings" beneath them, so the renderer collapses instead.
        var model = Model(5097.50m, 5000m, 0m);

        var lines = PayslipPdf.BuildEarningLines(model);

        Assert.Empty(lines);
        Assert.False(PayslipPdf.EarningsReconcile(model.Payslip, lines));
    }
}
