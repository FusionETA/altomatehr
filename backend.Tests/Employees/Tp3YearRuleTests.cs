using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;

namespace AltomateHR.Api.Tests.Employees;

// TP3 figures only count for the year they're entered against, so figures
// with no year would be silently ignored by payroll — saving refuses them.
public class Tp3YearRuleTests
{
    [Fact]
    public void FiguresWithoutAYear_AreRefused()
    {
        var problem = EmployeeProfileService.Tp3Problem(new EmployeeProfileDto { PrevPcb = 120m });
        Assert.Contains("enter the year", problem);
    }

    [Fact]
    public void AnItemByItemAmountWithoutAYear_IsRefusedToo()
    {
        var dto = new EmployeeProfileDto
        {
            PrevByCategoryJson = """[{"category":"deduct_tp1_life_insurance","amount":300}]""",
        };
        Assert.Contains("enter the year", EmployeeProfileService.Tp3Problem(dto));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void NoFiguresAndNoYear_IsFine(int? zero)
    {
        var dto = new EmployeeProfileDto { PrevRemuneration = zero is null ? null : 0m, PrevEpf = 0m };
        Assert.Null(EmployeeProfileService.Tp3Problem(dto));
    }

    [Fact]
    public void FiguresWithTheYear_AreFine()
    {
        var dto = new EmployeeProfileDto { PrevEmploymentYear = 2026, PrevRemuneration = 30000m, PrevPcb = 900m };
        Assert.Null(EmployeeProfileService.Tp3Problem(dto));
    }

    [Theory]
    [InlineData(26)]
    [InlineData(20266)]
    public void AYearThatIsntFourDigits_IsRefused(int year)
    {
        var problem = EmployeeProfileService.Tp3Problem(new EmployeeProfileDto { PrevEmploymentYear = year });
        Assert.Contains("4-digit year", problem);
    }
}
