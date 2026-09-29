using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// A previous employer's TP3 figures item by item, carried into the
// per-category year to date the yearly limits read.
public class PreviousEmployerItemsTests
{
    private static readonly IReadOnlyDictionary<string, decimal> Nothing = new Dictionary<string, decimal>();

    [Fact]
    public void Parse_KeepsOnlyItemsWithAYearlyLimit()
    {
        var items = PreviousEmployerItems.Parse("""
            [
              {"category":"allowance_travel_official","amount":3600},
              {"category":"deduct_tp1_lifestyle","amount":"1200"},
              {"category":"allowance_standard","amount":5000},
              {"category":"no_such_category","amount":10},
              {"category":"deduct_tp1_dental","amount":-5}
            ]
            """);

        Assert.Equal(
            ["allowance_travel_official", "deduct_tp1_lifestyle"],
            items.Select(i => i.Category));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Parse_ReadsNothingFromNothing(string? json) =>
        Assert.Empty(PreviousEmployerItems.Parse(json));

    // An exempt allowance only uses up its ceiling; a TP1 item is relief too.
    [Fact]
    public void Carry_AddsToTheYearToDate_AndOnlyTp1ItemsAreRelief()
    {
        var result = PreviousEmployerItems.Carry(
            [
                new("allowance_travel_official", 3600m),
                new("deduct_tp1_lifestyle", 1200m),
            ],
            new Dictionary<string, decimal> { ["allowance_travel_official"] = 500m });

        Assert.Equal(4100m, result.ByCategory["allowance_travel_official"]);
        Assert.Equal(1200m, result.ByCategory["deduct_tp1_lifestyle"]);
        Assert.Equal(1200m, result.Tp1Relief);
    }

    // An over-declared item counts as relief only up to its own limit.
    [Fact]
    public void Carry_ReliefStopsAtTheItemsLimit()
    {
        var result = PreviousEmployerItems.Carry([new("deduct_tp1_lifestyle", 4000m)], Nothing);

        Assert.Equal(2500m, result.Tp1Relief);
    }

    // A rehire whose TP3 already includes their earlier months here: only the
    // part beyond this org's own figure is added.
    [Fact]
    public void Carry_ForARehire_TakesThisOrgsOwnMonthsBackOut()
    {
        var result = PreviousEmployerItems.Carry(
            [new("allowance_travel_official", 3600m)],
            new Dictionary<string, decimal> { ["allowance_travel_official"] = 1000m },
            isPriorEmployerOnly: false);

        Assert.Equal(3600m, result.ByCategory["allowance_travel_official"]);
    }
}
