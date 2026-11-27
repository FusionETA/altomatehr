using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// The EPF wage — what KWSP's i-Akaun file calls "Member Wage". The calculator
// stamps it; PayslipEpfWage rebuilds it for payslips written before it was
// stored. These pin the two to each other, so a routing change in the
// calculator that the rebuild doesn't follow fails here rather than in a file
// someone uploads.
public class PayslipEpfWageTests
{
    private static PayslipCalculator.Input Make() => new()
    {
        PeriodYear = 2026,
        PeriodMonth = 11,
        SalaryType = SalaryType.MONTHLY,
        MonthlySalary = 8000m,
        FixedAllowances = [],
        Nationality = "Malaysian",
        DateOfBirth = new DateTime(1990, 6, 15),
        IsResident = true,
        EpfEmployeeRate = 11m,
        SocsoScheme = SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY,
        ContributeToEis = true,
        IncomeTaxNumber = "SG12345678",
        EpfNumber = "1234567",
        SocsoNumber = "900615012345",
        WorkingDaysRule = WorkingDaysRule.TWENTY_SIX,
        OtNormalHours = 0m,
        YtdTaxable = 0m,
        YtdEpf = 0m,
        YtdAllowanceByCategory = new Dictionary<string, decimal>(),
    };

    private static FixedAllowance Allowance(string category, decimal amount) => new()
    {
        Category = category,
        Name = null,
        Amount = amount,
        TreatAsRecurring = false,
    };

    // The calculator's own line items, as the payslip table stores them.
    private static List<PayslipLineItem> Stored(PayslipCalculator.Result r) =>
        [.. r.LineItems.Select(l => new PayslipLineItem
        {
            Kind = l.Kind,
            Label = l.Label,
            Amount = l.Amount,
            Category = l.Category,
            ClaimId = l.ClaimId,
            SubjectToEpf = l.SubjectToEpf,
            SubjectToSocso = l.SubjectToSocso,
            SubjectToEis = l.SubjectToEis,
            SubjectToPcb = l.SubjectToPcb,
        })];

    public static TheoryData<string> EveryCategory()
    {
        var data = new TheoryData<string>();
        foreach (var code in PayrollAdjustmentCategories.All.Keys.Order(StringComparer.Ordinal))
            data.Add(code);
        return data;
    }

    // Every catalogue category, alone on top of basic, with a claim, a manual
    // deduction and overtime alongside — none of which are EPF wage.
    [Theory]
    [MemberData(nameof(EveryCategory))]
    public void Rebuild_MatchesTheCalculator_ForEveryCategory(string category)
    {
        var r = PayslipCalculator.Calculate(Make() with
        {
            FixedAllowances = [Allowance(category, 300m)],
            Reimbursements = [new PayslipCalculator.Reimbursement("claim-1", "Taxi", 193.72m)],
            ManualDeductions = [new PayslipCalculator.ManualDeduction("Canteen", 45m)],
            OtNormalHours = 4m,
        });

        Assert.Equal(r.EpfWage, PayslipEpfWage.Rebuild(r.ProratedPay, Stored(r)));
    }

    // The case that was reported: RM 8,000 basic, an EPF-able allowance, one
    // that isn't, and a RM 193.72 claim. Gross is 8,993.72; EPF was worked out
    // on 8,300 — and that is the figure the file must carry.
    [Fact]
    public void ClaimsAndNonEpfAllowances_StayOutOfTheEpfWage()
    {
        var r = PayslipCalculator.Calculate(Make() with
        {
            FixedAllowances =
            [
                Allowance(PayrollAdjustmentCategories.AllowanceStandard, 300m),
                Allowance(PayrollAdjustmentCategories.AllowanceTravelOfficial, 500m),
            ],
            Reimbursements = [new PayslipCalculator.Reimbursement("claim-1", "Claim", 193.72m)],
        });

        Assert.Equal(8993.72m, r.GrossPay);
        Assert.Equal(8300m, r.EpfWage);
        Assert.Equal(8300m, PayslipEpfWage.Rebuild(r.ProratedPay, Stored(r)));
    }

    [Fact]
    public void AStoredWage_WinsOverTheRebuild()
    {
        var payslip = new Payslip { ProratedPay = 8000m, EpfWage = 7777m };

        Assert.Equal(7777m, PayslipEpfWage.For(payslip, []));
    }
}
