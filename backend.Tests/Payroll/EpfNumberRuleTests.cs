using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// KWSP's upload refuses a non-numeric EPF number; a placeholder like
// "XXXXXX" reached the file because nothing checked it.
public class EpfNumberRuleTests
{
    [Theory]
    [InlineData("21968561")]
    [InlineData("03437018")]
    [InlineData(" 2542950 ")]   // short, but numeric — length isn't KWSP-published
    [InlineData(null)]
    [InlineData("")]
    public void ANumericOrBlankNumber_IsFine(string? epf) => Assert.Null(EpfNumberRule.Problem(epf));

    [Theory]
    [InlineData("XXXXXX")]
    [InlineData("EP12345")]
    [InlineData("2000-001")]
    [InlineData("1234 5678")]
    public void APlaceholderOrNonDigits_IsRefused(string epf) =>
        Assert.Equal(EpfNumberRule.Message, EpfNumberRule.Problem(epf));

    // Submission is refused for it, naming the field, like a missing IC.
    [Fact]
    public void ReadinessFlagsANonNumericEpfNumber()
    {
        var gaps = PayrollRunReadiness.EmployeeGaps("00448", "060503120798", isLocalOrPr: true, epfNumber: "XXXXXX");
        Assert.Equal(["EPF number (digits only)"], gaps);

        Assert.Empty(PayrollRunReadiness.EmployeeGaps("00448", "060503120798", isLocalOrPr: true, epfNumber: "24469760"));
    }
}
