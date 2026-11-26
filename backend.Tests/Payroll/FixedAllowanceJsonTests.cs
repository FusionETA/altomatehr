using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// One bad row used to drop the whole list — every recurring allowance left
// gross, EPF and PCB with no warning. Each row is now read on its own.
public class FixedAllowanceJsonTests
{
    [Fact]
    public void ABlankAmount_KeepsTheOtherRows_AndTheirPositions()
    {
        var rows = FixedAllowanceJson.Parse(
            """[{"category":"allowance_standard","name":"Travel","amount":300},{"category":"allowance_standard","name":"Phone","amount":null},{"category":"allowance_meal","amount":200}]""");

        Assert.Equal(3, rows.Count);
        Assert.Equal(300m, rows[0].Amount);
        Assert.Equal(0m, rows[1].Amount);       // the blank row pays nothing
        Assert.Equal("Phone", rows[1].Name);
        Assert.Equal(200m, rows[2].Amount);     // still index 2, so overrides keyed "2" still apply
        Assert.Equal(PayrollAdjustmentCategories.AllowanceMeal, rows[2].Category);
    }

    [Fact]
    public void ANullElement_BecomesAnEmptyRow_NotACrash()
    {
        var rows = FixedAllowanceJson.Parse("""[null,{"category":"allowance_meal","amount":150}]""");

        Assert.Equal(2, rows.Count);
        Assert.Equal(0m, rows[0].Amount);
        Assert.Equal(150m, rows[1].Amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"category":"allowance_meal"}""")]
    public void AnythingButAnArray_ReadsAsNoRows(string? json)
    {
        Assert.Empty(FixedAllowanceJson.Parse(json));
    }

    [Fact]
    public void AnAmountWrittenAsText_IsRead()
    {
        var rows = FixedAllowanceJson.Parse("""[{"category":"allowance_standard","amount":"120.50","treatAsRecurring":true}]""");

        Assert.Equal(120.50m, rows[0].Amount);
        Assert.True(rows[0].TreatAsRecurring);
    }

    [Fact]
    public void TheRunEditorsRows_CarryTheArrayIndex()
    {
        var rows = PayrollRunAdjustmentService.ParseFixedAllowances(
            """[{"category":"allowance_standard","amount":null},{"category":"allowance_meal","amount":200}]""");

        Assert.Equal([0, 1], rows.Select(r => r.Index));
        Assert.Equal(200m, rows[1].Amount);
    }
}
