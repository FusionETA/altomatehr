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

    // A stored cost higher than the identity allows can only fall back to
    // GrossPay, never show more than was earned.
    [Fact]
    public void OddStoredFigures_FallBackToGrossPay()
    {
        var p = new Payslip { GrossPay = 1000m, EpfEmployer = 130m, TotalCostToEmployer = 2000m };

        Assert.Equal(1000m, PayrollGrossShown.Of(p));
    }
}
