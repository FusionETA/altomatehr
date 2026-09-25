using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// Free-text nationality onto the dropdown's demonyms. The CP39 file keys
// locals on an exact "Malaysian" and the LHDN country code is looked up by
// demonym, so a spreadsheet's "Malaysia" or "China" has to land as the demonym.
public class NationalitiesTests
{
    [Theory]
    [InlineData("Malaysian", "Malaysian")]
    [InlineData("  malaysian ", "Malaysian")]
    [InlineData("Malaysia", "Malaysian")]
    [InlineData("MYS", "Malaysian")]
    [InlineData("Rakyat Malaysia", "Malaysian")]
    [InlineData("China", "Chinese")]
    [InlineData("PRC", "Chinese")]
    [InlineData("Australia", "Australian")]
    [InlineData("Singapore", "Singaporean")]
    [InlineData("United Kingdom", "British")]
    [InlineData("USA", "American")]
    [InlineData("Myanmar", "Burmese")]
    [InlineData("Turkey", "Turkish")]
    [InlineData("Ivory Coast", "Ivorian")]
    [InlineData("Côte d’Ivoire", "Ivorian")]
    [InlineData("Philippines", "Filipino")]
    public void AKnownSpellingResolvesToTheDemonym(string typed, string expected)
    {
        var (value, recognised) = Nationalities.Resolve(typed);

        Assert.True(recognised);
        Assert.Equal(expected, value);
    }

    // Kept as typed and flagged, never guessed. "NA" is the case that rules
    // out accepting two-letter codes generally — it would become Namibian.
    [Theory]
    [InlineData("Malaysian / Indonesian / etc.")]
    [InlineData("NA")]
    [InlineData("Martian")]
    public void AnUnknownValueIsKeptAsTyped(string typed)
    {
        var (value, recognised) = Nationalities.Resolve($" {typed} ");

        Assert.False(recognised);
        Assert.Equal(typed, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankIsNull(string? typed)
    {
        Assert.Null(Nationalities.Normalise(typed));
    }

    // Every alias must land on something the dropdown offers, or a mapped
    // value would still be "not on the list".
    [Fact]
    public void EveryResolvedValueIsOnTheList()
    {
        var list = NationalityCountryCodes.Demonyms.ToHashSet();

        foreach (var typed in new[] { "Malaysia", "China", "UK", "UAE", "Holland", "Korea", "Burma" })
        {
            Assert.Contains(Nationalities.Normalise(typed)!, list);
        }
    }
}
