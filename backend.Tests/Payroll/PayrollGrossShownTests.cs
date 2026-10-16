using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

public class PayrollGrossShownTests
{
    // Muhammad Afiq's September as PayrollPanda shows it: earned 9,555, a
    // RM 175 other deduction kept by the company → gross 9,380, while cost
    // (10,902.75) = 9,380 + employer EPF/SOCSO/EIS.
    [Fact]
    public void TakesOffWhatTheCompanyKeeps_AsPayrollPandaShowsIt()
    {
        var p = new Payslip
        {
            GrossPay = 9555m, EpfEmployer = 1434m, SocsoEmployer = 79.65m, EisEmployer = 9.10m, Hrdf = 0m,
            TotalCostToEmployer = 10902.75m,
        };

        Assert.Equal(9380m, PayrollGrossShown.Of(p));
    }

    [Fact]
    public void NothingKept_ShowsGrossPay()
    {
        var p = new Payslip
        {
            GrossPay = 4537m, EpfEmployer = 681m, SocsoEmployer = 79.65m, EisEmployer = 9.10m, Hrdf = 45.37m,
            TotalCostToEmployer = 5352.12m,
        };

        Assert.Equal(4537m, PayrollGrossShown.Of(p));
    }

    // Not clamped, so a run's total always equals its rows added up — even
    // when a row's kept deductions exceed its pay and it shows below zero.
    [Fact]
    public void RunTotal_EqualsItsRowsAddedUp_EvenWhenARowGoesBelowZero()
    {
        var a = new Payslip { GrossPay = 3000m, EpfEmployer = 390m, TotalCostToEmployer = 3000m + 390m - 4000m };
        var b = new Payslip { GrossPay = 5000m, EpfEmployer = 650m, TotalCostToEmployer = 5650m };
        var run = new PayrollRun
        {
            TotalGross = 8000m, TotalEmployerEpf = 1040m,
            TotalCostToEmployer = a.TotalCostToEmployer + b.TotalCostToEmployer,
        };

        Assert.Equal(-1000m, PayrollGrossShown.Of(a));
        Assert.Equal(PayrollGrossShown.Of(a) + PayrollGrossShown.Of(b), PayrollGrossShown.Of(run));
    }
}
