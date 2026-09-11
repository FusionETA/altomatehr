using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

public class MoneyTests
{
    // Round2 must be commercial (half away from zero), NOT .NET's default
    // banker's rounding, which would under-collect on half-sen amounts.
    [Theory]
    [InlineData(0.125, 0.13)]
    [InlineData(0.135, 0.14)]   // banker's would give 0.14 too…
    [InlineData(0.145, 0.15)]   // …but banker's gives 0.14 here
    [InlineData(2.345, 2.35)]
    [InlineData(-0.125, -0.13)]
    [InlineData(514.166666, 514.17)]
    public void Round2_RoundsHalfAwayFromZero(decimal value, decimal expected)
    {
        Assert.Equal(expected, Money.Round2(value));
    }

    // ─── Trunc2 ─────────────────────────────────────────────────────────

    // These are the values that broke the reference implementation's float
    // version: 32.55 × 100 is 3254.9999999999995 in IEEE 754, so a naive
    // truncate returned 32.54 and silently changed a figure on an LHDN PDF.
    // Decimal is exact, so the plain form is correct — but the cases stay as
    // regression cover in case anyone reaches for double here later.
    [Theory]
    [InlineData(32.55, 32.55)]
    [InlineData(41567.52, 41567.52)]
    [InlineData(0.07, 0.07)]
    [InlineData(0.1, 0.1)]
    [InlineData(0.2, 0.2)]
    [InlineData(156.05, 156.05)]
    [InlineData(1067.13, 1067.13)]
    [InlineData(994.05, 994.05)]
    public void Trunc2_PreservesCleanTwoDecimalValues(decimal value, decimal expected)
    {
        Assert.Equal(expected, Money.Trunc2(value));
    }

    // Real precision past 2dp is dropped, not rounded — LHDN's convention
    // wherever the spec says further figures are "omitted".
    [Fact]
    public void Trunc2_TruncatesTheK2Division()
    {
        Assert.Equal(322.63m, Money.Trunc2(3549m / 11m));
    }

    [Fact]
    public void Trunc2_TruncatesTheCurrentMonthPcbDivision()
    {
        Assert.Equal(15.09m, Money.Trunc2(181.15m / 12m));
    }

    [Fact]
    public void Trunc2_WouldRoundUpIfItRounded()
    {
        // 0.999 truncates to 0.99. Rounding would give 1.00.
        Assert.Equal(0.99m, Money.Trunc2(0.999m));
    }

    // Truncation is toward zero, so -0.07 stays put rather than drifting.
    [Theory]
    [InlineData(-0.07, -0.07)]
    [InlineData(-250, -250)]
    [InlineData(-15.0958, -15.09)]
    [InlineData(0, 0)]
    public void Trunc2_TruncatesTowardZero(decimal value, decimal expected)
    {
        Assert.Equal(expected, Money.Trunc2(value));
    }

    // ─── CeilRinggit ────────────────────────────────────────────────────

    [Theory]
    [InlineData(4100, 13, 533)]      // exact — no rounding needed
    [InlineData(4100, 11, 451)]
    [InlineData(100, 6.5, 7)]        // 6.50 → 7
    [InlineData(13946, 2, 279)]      // 278.92 → 279
    [InlineData(25750, 11, 2833)]    // 2832.50 → 2833
    public void CeilRinggit_RoundsUpToTheNextWholeRinggit(
        decimal amount, decimal rate, decimal expected)
    {
        Assert.Equal(expected, Money.CeilRinggit(amount, rate));
    }

    [Theory]
    [InlineData(0, 11)]
    [InlineData(-500, 11)]
    [InlineData(4100, 0)]
    public void CeilRinggit_IsZeroWithNothingToCharge(decimal amount, decimal rate)
    {
        Assert.Equal(0m, Money.CeilRinggit(amount, rate));
    }
}
