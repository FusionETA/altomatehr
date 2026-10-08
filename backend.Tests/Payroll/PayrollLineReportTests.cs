using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Payroll.Pdf;
using ClosedXML.Excel;
using static AltomateHR.Api.Modules.Payroll.PayrollLineReportXlsx;

namespace AltomateHR.Api.Tests.Payroll;

// The Allowance and Deduction reports: which payslip lines each one carries,
// and that the sheet adds up.
public class PayrollLineReportTests
{
    [Fact]
    public void Allowances_report_every_allowance_and_keeps_benefits_in_kind_out_of_the_total()
    {
        var model = Model(
            ("ps-1", "Aisyah", [
                Line(PayslipLineKind.ALLOWANCE, "Transport", 200m, PayrollAdjustmentCategories.AllowanceStandard, 1),
                Line(PayslipLineKind.ALLOWANCE, "Company car", 500m, PayrollAdjustmentCategories.BikCar, 2),
                Line(PayslipLineKind.REIMBURSEMENT, "Claim: taxi", 40m, null, 3),
                Line(PayslipLineKind.DEDUCTION, "Loan", 100m, PayrollAdjustmentCategories.DeductLoanRepayment, 4),
            ]),
            ("ps-2", "Badrul", [
                Line(PayslipLineKind.ALLOWANCE, "Transport", 150m, PayrollAdjustmentCategories.AllowanceStandard, 1),
            ]));

        var sheet = Open(Render(model, Report.Allowances));

        // Emp no., Employee, Transport, Company car (BIK), Total.
        Assert.Equal("Transport", sheet.Cell(4, 3).GetString());
        Assert.Equal("Company car (BIK)", sheet.Cell(4, 4).GetString());
        Assert.Equal(200m, sheet.Cell(5, 3).GetValue<decimal>());
        Assert.Equal(200m, sheet.Cell(5, 5).GetValue<decimal>());   // BIK not in the cash total
        Assert.Equal(150m, sheet.Cell(6, 5).GetValue<decimal>());
        Assert.Equal(350m, sheet.Cell(7, 3).GetValue<decimal>());   // column total
        Assert.Equal(350m, sheet.Cell(7, 5).GetValue<decimal>());
        Assert.True(sheet.Cell(4, 6).IsEmpty());                     // no claim column
    }

    [Fact]
    public void Deductions_leave_out_tax_and_reliefs_that_take_nothing_off_pay()
    {
        var lines = new[]
        {
            Line(PayslipLineKind.DEDUCTION, "Loan", 100m, PayrollAdjustmentCategories.DeductLoanRepayment, 1),
            Line(PayslipLineKind.DEDUCTION, "Unpaid leave", 80m, PayrollAdjustmentCategories.DeductUnpaidLeave, 2),
            Line(PayslipLineKind.DEDUCTION, "Uniform", 30m, null, 3),
            Line(PayslipLineKind.DEDUCTION, "TP1 life insurance", 300m, PayrollAdjustmentCategories.DeductTp1LifeInsurance, 4),
            Line(PayslipLineKind.DEDUCTION, "Additional PCB", 50m, PayrollAdjustmentCategories.DeductAdditionalPcb, 5),
            Line(PayslipLineKind.DEDUCTION, "CP38", 60m, PayrollAdjustmentCategories.DeductCp38, 6),
        };

        Assert.Equal(["Loan", "Unpaid leave", "Uniform"],
            lines.Where(IsOtherDeduction).Select(l => l.Label).ToArray());

        var sheet = Open(Render(Model(("ps-1", "Aisyah", lines)), Report.Deductions));
        Assert.Equal(210m, sheet.Cell(5, 6).GetValue<decimal>());
    }

    [Fact]
    public void Someone_with_nothing_to_report_is_left_off_and_an_empty_run_has_nothing()
    {
        var model = Model(
            ("ps-1", "Aisyah", [Line(PayslipLineKind.ALLOWANCE, "Transport", 200m, PayrollAdjustmentCategories.AllowanceStandard, 1)]),
            ("ps-2", "Badrul", []));

        var sheet = Open(Render(model, Report.Allowances));
        Assert.Equal("Aisyah", sheet.Cell(5, 2).GetString());
        Assert.Equal("Total (1)", sheet.Cell(6, 2).GetString());

        Assert.False(HasAny(model, Report.Deductions));
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static PayslipLineItem Line(PayslipLineKind kind, string label, decimal amount, string? category, int order) =>
        new() { Kind = kind, Label = label, Amount = amount, Category = category, SortOrder = order };

    private static PayrollDocumentModel Model(params (string PayslipId, string Name, PayslipLineItem[] Lines)[] people) => new()
    {
        Run = new PayrollRun { Id = "run-1", OrganizationId = "org-1", PeriodYear = 2026, PeriodMonth = 3, Status = PayrollRunStatus.SUBMITTED },
        OrganizationName = "Globe Engineering Sdn Bhd",
        PeriodLabel = "March 2026",
        StatusLabel = "Submitted",
        IssueDate = new DateTime(2026, 3, 31),
        Rows = people.Select(p => new StatutoryEmployeeRow
        {
            Payslip = new Payslip { Id = p.PayslipId, OrganizationId = "org-1", PayrollRunId = "run-1" },
            EmployeeName = p.Name,
            EmployeeCode = p.PayslipId,
        }).ToList(),
        LineItems = people.ToDictionary(p => p.PayslipId, p => (IReadOnlyList<PayslipLineItem>)p.Lines),
    };

    private static IXLWorksheet Open(byte[] bytes) => new XLWorkbook(new MemoryStream(bytes)).Worksheet(1);
}
