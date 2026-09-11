using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// The merge that turns "the employee's profile" plus "what the admin typed for
// this run" into the single list the calculator prices.
//
// Small surface, but it is where a per-run tweak can quietly change an
// employee's STATUTORY treatment rather than just their money — so most of
// what is asserted here is what must NOT change.
public class PayrollRunAdjustmentsTests
{
    private static FixedAllowance Travel(decimal amount = 500m) => new()
    {
        Category = PayrollAdjustmentCategories.AllowanceTravelOfficial,
        Name = "Travel",
        Amount = amount,
    };

    private static FixedAllowance Standard(decimal amount = 100m) => new()
    {
        Category = PayrollAdjustmentCategories.AllowanceStandard,
        Name = "Standard",
        Amount = amount,
    };

    private static PayrollRunAdjustment Adjustment(
        string? overridesJson = null, string? manualJson = null) => new()
    {
        FixedAllowanceOverridesJson = overridesJson ?? "{}",
        ManualLineItemsJson = manualJson ?? "[]",
    };

    // ─── Overrides ──────────────────────────────────────────────────────

    // THE trap. An override carries an amount and nothing else, so everything
    // that decides statutory treatment has to survive it. Overriding the
    // amount while losing the category would silently move a travel allowance
    // — exempt up to RM 6,000 a year and outside the EPF base — into the EPF
    // base as a plain allowance, and nothing on the payslip would say so.
    [Fact]
    public void ApplyOverrides_KeepsTheOriginalCategoryWhenTheAmountIsReplaced()
    {
        var result = PayrollRunAdjustments.ApplyOverrides(
            [Travel(500m)],
            new Dictionary<string, FixedAllowanceOverride>
            {
                ["0"] = new() { Amount = 250m },
            });

        var row = Assert.Single(result);
        Assert.Equal(250m, row.Amount);
        Assert.Equal(PayrollAdjustmentCategories.AllowanceTravelOfficial, row.Category);
        Assert.Equal("Travel", row.Name);
    }

    [Fact]
    public void ApplyOverrides_SkipDropsTheRowForThisRunOnly()
    {
        var result = PayrollRunAdjustments.ApplyOverrides(
            [Travel(), Standard()],
            new Dictionary<string, FixedAllowanceOverride> { ["0"] = new() { Skip = true } });

        var row = Assert.Single(result);
        Assert.Equal(PayrollAdjustmentCategories.AllowanceStandard, row.Category);
    }

    // Skip is the stronger instruction: an admin who zeroed a row and also
    // typed an amount into it meant to zero it.
    [Fact]
    public void ApplyOverrides_SkipWinsOverAnAmount()
    {
        var result = PayrollRunAdjustments.ApplyOverrides(
            [Travel()],
            new Dictionary<string, FixedAllowanceOverride>
            {
                ["0"] = new() { Skip = true, Amount = 999m },
            });

        Assert.Empty(result);
    }

    [Fact]
    public void ApplyOverrides_LeavesUnmentionedRowsAtTheProfileAmount()
    {
        var result = PayrollRunAdjustments.ApplyOverrides(
            [Travel(500m), Standard(100m)],
            new Dictionary<string, FixedAllowanceOverride> { ["1"] = new() { Amount = 30m } });

        Assert.Equal(500m, result[0].Amount);
        Assert.Equal(30m, result[1].Amount);
    }

    // The key is a POSITION, not an identity. If the profile lost a row after
    // the override was saved, the stale index points past the end. Ignoring it
    // pays the profile's rows as they stand, which is the safe direction — the
    // alternative is throwing in the middle of a month's payroll.
    [Fact]
    public void ApplyOverrides_IgnoresAnIndexPastTheEndOfTheArray()
    {
        var result = PayrollRunAdjustments.ApplyOverrides(
            [Travel(500m)],
            new Dictionary<string, FixedAllowanceOverride>
            {
                ["7"] = new() { Amount = 1m },
                ["-1"] = new() { Skip = true },
                ["not-a-number"] = new() { Skip = true },
            });

        var row = Assert.Single(result);
        Assert.Equal(500m, row.Amount);
    }

    // An override amount of zero is a real instruction ("pay nothing this
    // month"), not an absent one. The calculator drops non-positive rows, so
    // this has to reach it as 0 rather than fall back to the profile's 500.
    [Fact]
    public void ApplyOverrides_TreatsAZeroAmountAsAnOverrideRatherThanAsAbsent()
    {
        var result = PayrollRunAdjustments.ApplyOverrides(
            [Travel(500m)],
            new Dictionary<string, FixedAllowanceOverride> { ["0"] = new() { Amount = 0m } });

        Assert.Equal(0m, Assert.Single(result).Amount);
    }

    // ─── Manual line items ──────────────────────────────────────────────

    [Fact]
    public void Merge_AppendsManualRowsAfterTheProfileRows()
    {
        var adjustment = Adjustment(manualJson: """
            [{"kind":"DEDUCTION","category":"deduct_advance","label":"Salary advance","amount":200}]
            """);

        var result = PayrollRunAdjustments.Merge([Travel()], adjustment);

        Assert.Equal(2, result.Count);
        Assert.Equal(PayrollAdjustmentCategories.AllowanceTravelOfficial, result[0].Category);
        Assert.Equal("deduct_advance", result[1].Category);
        Assert.Equal("Salary advance", result[1].Name);
        Assert.Equal(200m, result[1].Amount);
    }

    // The AR override has to survive the merge or a monthly commission goes
    // back to being taxed as a one-off spike.
    [Fact]
    public void Merge_CarriesTreatAsRecurringThrough()
    {
        var adjustment = Adjustment(manualJson: """
            [{"category":"wages_commission","label":"Commission","amount":800,"treatAsRecurring":true}]
            """);

        var result = PayrollRunAdjustments.Merge([], adjustment);

        Assert.True(Assert.Single(result).TreatAsRecurring);
    }

    [Fact]
    public void Merge_AppliesOverridesAndManualRowsTogether()
    {
        var adjustment = Adjustment(
            overridesJson: """{"0":{"skip":true}}""",
            manualJson: """[{"category":"wages_bonus_annual","label":"Bonus","amount":1000}]""");

        var result = PayrollRunAdjustments.Merge([Travel(), Standard()], adjustment);

        Assert.Equal(2, result.Count);
        Assert.Equal(PayrollAdjustmentCategories.AllowanceStandard, result[0].Category);
        Assert.Equal("wages_bonus_annual", result[1].Category);
    }

    [Fact]
    public void Merge_WithNoAdjustmentReturnsTheProfileRowsUnchanged()
    {
        var profile = new List<FixedAllowance> { Travel(), Standard() };

        var result = PayrollRunAdjustments.Merge(profile, null);

        Assert.Equal(2, result.Count);
        Assert.Equal(500m, result[0].Amount);
    }

    // ─── Defensive parsing ──────────────────────────────────────────────

    // One malformed blob must not take a whole month's payroll down. Reading it
    // as empty under-pays that one row, which an admin can see and fix; an
    // exception here stops everyone getting paid.
    [Theory]
    [InlineData("not json")]
    [InlineData("{")]
    [InlineData("")]
    [InlineData(null)]
    public void Merge_SurvivesMalformedJson(string? json)
    {
        var adjustment = Adjustment(overridesJson: json, manualJson: json);

        var result = PayrollRunAdjustments.Merge([Travel()], adjustment);

        Assert.Equal(500m, Assert.Single(result).Amount);
    }

    // The JSON is written camelCase and read back case-insensitively, so a blob
    // from either convention round-trips.
    [Fact]
    public void ParseManualLineItems_ReadsEitherCasing()
    {
        var camel = PayrollRunAdjustments.ParseManualLineItems(
            """[{"category":"wages_bonus_annual","label":"B","amount":10}]""");
        var pascal = PayrollRunAdjustments.ParseManualLineItems(
            """[{"Category":"wages_bonus_annual","Label":"B","Amount":10}]""");

        Assert.Equal(10m, Assert.Single(camel).Amount);
        Assert.Equal(10m, Assert.Single(pascal).Amount);
    }

    [Fact]
    public void Serialize_RoundTripsThroughTheParsers()
    {
        var items = new List<ManualLineItem>
        {
            new()
            {
                Kind = PayslipLineKind.DEDUCTION,
                Category = "deduct_advance",
                Label = "Advance",
                Amount = 150.55m,
                TreatAsRecurring = true,
            },
        };

        var parsed = PayrollRunAdjustments.ParseManualLineItems(
            PayrollRunAdjustments.Serialize(items));

        var row = Assert.Single(parsed);
        Assert.Equal(PayslipLineKind.DEDUCTION, row.Kind);
        Assert.Equal("deduct_advance", row.Category);
        Assert.Equal(150.55m, row.Amount);
        Assert.True(row.TreatAsRecurring);
    }
}
