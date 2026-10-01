using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// A citizen migrated from v1 could carry IsResident = false and was taxed at
// the 30% non-resident rate while the profile screen showed them as resident.
public class TaxResidencyTests
{
    [Theory]
    [InlineData(false, "Malaysian")]
    [InlineData(false, "malaysia")]
    [InlineData(false, "Warganegara Malaysia")]
    [InlineData(false, "Malaysian Citizen")]
    [InlineData(false, "warganegara")]
    [InlineData(false, "MY")]
    [InlineData(true, "Malaysian")]
    public void A_Malaysian_is_always_a_tax_resident(bool stored, string nationality) =>
        Assert.True(PayslipCalculator.IsTaxResident(stored, nationality));

    [Theory]
    [InlineData(false, "Chinese", false)]
    [InlineData(true, "Chinese", true)]
    [InlineData(false, null, false)]
    [InlineData(false, "", false)]
    public void A_non_citizen_keeps_the_stored_flag(bool stored, string? nationality, bool expected) =>
        Assert.Equal(expected, PayslipCalculator.IsTaxResident(stored, nationality));
}
