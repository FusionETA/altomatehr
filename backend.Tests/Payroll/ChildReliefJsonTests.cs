using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// Profiles migrated from v1 carry v1's legacy child shapes. A strict read
// rejected them and dropped every child, so PCB ran with C = 0 while the
// profile screen still listed the children.
public class ChildReliefJsonTests
{
    private static decimal Relief(string json) =>
        PcbReliefs.Itemise(false, null, null, ChildReliefJson.Parse(json)).Children;

    [Fact]
    public void A_null_studying_level_with_an_age_is_an_under_18_child()
    {
        // The exact shape v1 stored before its July 2026 simplification.
        var json = """[{"age": 18, "pcbDeduction": "FULL", "abilityStatus": "NORMAL", "currentlyStudying": null}]""";

        var child = Assert.Single(ChildReliefJson.Parse(json));
        Assert.Equal(ChildStudyingLevel.UNDER_18, child.CurrentlyStudying);
        Assert.Equal(ChildPcbDeductionLevel.FULL, child.PcbDeduction);
        Assert.Equal(2000m, Relief(json));
    }

    [Theory]
    [InlineData("PRESCHOOL", 2000)]
    [InlineData("PRIMARY", 2000)]
    [InlineData("SECONDARY", 2000)]
    [InlineData("NONE", 2000)]
    [InlineData("HIGHER_ED", 8000)]
    [InlineData("PRE_UNIVERSITY", 2000)]
    [InlineData("DIPLOMA_MALAYSIA", 8000)]
    [InlineData("DEGREE_ABROAD", 8000)]
    public void Legacy_and_current_studying_levels_map_as_v1_did(string level, int relief)
    {
        var json = $$"""[{"abilityStatus":"NORMAL","currentlyStudying":"{{level}}","pcbDeduction":"FULL"}]""";
        Assert.Equal(relief, Relief(json));
    }

    [Fact]
    public void One_legacy_child_no_longer_drops_the_others()
    {
        var json = """
            [{"abilityStatus":"NORMAL","currentlyStudying":"UNDER_18","pcbDeduction":"FULL"},
             {"abilityStatus":"DISABLED","currentlyStudying":"PRIMARY","pcbDeduction":"HALF"}]
            """;

        Assert.Equal(2, ChildReliefJson.Parse(json).Count);
        Assert.Equal(2000m + 4000m, Relief(json));
    }

    [Fact]
    public void A_missing_or_unknown_claim_is_no_claim()
    {
        Assert.Equal(0m, Relief("""[{"currentlyStudying":"UNDER_18"}]"""));
        Assert.Equal(0m, Relief("""[{"currentlyStudying":"UNDER_18","pcbDeduction":"MAYBE"}]"""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    public void Unreadable_json_is_no_children(string? json)
    {
        Assert.Empty(ChildReliefJson.Parse(json));
    }
}
