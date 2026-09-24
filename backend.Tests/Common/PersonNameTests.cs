using AltomateHR.Api.Common;

namespace AltomateHR.Api.Tests.Common;

// `Name ?? Email` only skips a null; an empty name used to come through as a
// blank cell. These pin the fallback that replaces it.
public class PersonNameTests
{
    [Fact]
    public void UsesTheNameWhenThereIsOne() =>
        Assert.Equal("Aisyah", PersonName.Display("Aisyah", "a@x.com"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FallsBackToTheEmailWhenTheNameIsBlank(string? name) =>
        Assert.Equal("a@x.com", PersonName.Display(name, "a@x.com"));

    [Fact]
    public void FallsBackToTheGivenValueWhenBothAreBlank() =>
        Assert.Equal("emp-1", PersonName.Display("", " ", "emp-1"));

    [Fact]
    public void TrimsWhatItReturns() =>
        Assert.Equal("Aisyah", PersonName.Display("  Aisyah ", null));
}
