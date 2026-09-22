using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// Which bank's layout a run's upload file is rendered in. Getting this wrong
// is silent: the admin downloads a file, the portal rejects it, and nothing
// says the layout belonged to a different bank.
public class PayrollDisbursementTests
{
    [Theory]
    [InlineData("Public Bank Berhad", PayrollFileFormat.PbEcpXlsx)]
    [InlineData("Malayan Banking Berhad", PayrollFileFormat.MbbM2eTxt)]
    [InlineData("CIMB Bank Berhad", PayrollFileFormat.CimbBizChannelTxt)]
    [InlineData("Hong Leong Bank Berhad", PayrollFileFormat.HlbConnect)]
    public void EachSupportedBankResolvesToItsOwnFormat(string bank, PayrollFileFormat expected) =>
        Assert.Equal(expected, PayrollDisbursement.FormatFor(bank));

    [Theory]
    // Typed before the picker existed, or by an admin who knows the short name.
    [InlineData("maybank")]
    [InlineData("MAYBANK")]
    [InlineData(" Malayan Banking Berhad ")]
    public void ANameTypedLooselyStillResolves(string bank) =>
        Assert.Equal(PayrollFileFormat.MbbM2eTxt, PayrollDisbursement.FormatFor(bank));

    // Null and "Other" both produce no file, but they are different states:
    // null is "nobody has configured this", which is worth nagging about;
    // "Other" is a decision already taken.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Other")]
    [InlineData("Bank Rakyat")]          // real bank, no bulk format
    [InlineData("not a bank at all")]
    public void AnythingWithoutANativeFormatResolvesToNull(string? bank) =>
        Assert.Null(PayrollDisbursement.FormatFor(bank));

    [Fact]
    public void TheIslamicArmIsNotOfferedSeparately()
    {
        // It files identically to its conventional parent, so offering both
        // would ask an admin to choose between two answers that do the same
        // thing. One option per FORMAT is the rule.
        Assert.DoesNotContain(PayrollDisbursement.Options, o => o.Label.Contains("Islamic Berhad"));
        Assert.Equal(5, PayrollDisbursement.Options.Count);

        var formats = PayrollDisbursement.Options
            .Where(o => o.Format is not null)
            .Select(o => o.Format!.Value)
            .ToList();
        Assert.Equal(formats.Count, formats.Distinct().Count());
    }

    [Fact]
    public void EveryOfferedOptionActuallyResolves()
    {
        // A picker that offers a value FormatFor can't resolve would leave an
        // admin unable to get a file after choosing correctly.
        foreach (var (value, _, format) in PayrollDisbursement.Options)
        {
            Assert.Equal(format, PayrollDisbursement.FormatFor(value));
        }
    }
}
