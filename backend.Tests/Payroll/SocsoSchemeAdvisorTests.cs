using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// The citizenship branches matter post Oct-2025: foreign workers now fall under
// SOCSO the same way citizens do, but the 55–59 first-time-registrant ambiguity
// applies only to legacy citizen members, so the dropdown auto-fills cleanly for
// foreign workers right through to 60.
public class SocsoSchemeAdvisorTests
{
    // Fixed reference date so the ages below are deterministic.
    private static readonly DateTime AsOf = new(2026, 6, 22);
    private static readonly DateTime DobAge40 = new(1986, 1, 1);
    private static readonly DateTime DobAge57 = new(1969, 1, 1);
    private static readonly DateTime DobAge65 = new(1961, 1, 1);

    [Theory]
    [InlineData(true)]    // Malaysian
    [InlineData(false)]   // foreign worker
    [InlineData(null)]    // nationality not captured yet
    public void Under55_IsScheme1_WhateverTheNationality(bool? isMalaysian)
    {
        Assert.Equal(
            SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY,
            SocsoSchemeAdvisor.Recommend(DobAge40, isMalaysian, AsOf));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void SixtyAndOver_IsScheme2_AgeDominates(bool? isMalaysian)
    {
        Assert.Equal(
            SocsoScheme.EMPLOYMENT_INJURY_ONLY,
            SocsoSchemeAdvisor.Recommend(DobAge65, isMalaysian, AsOf));
    }

    [Fact]
    public void Malaysian55To59_IsAmbiguous()
    {
        Assert.Null(SocsoSchemeAdvisor.Recommend(DobAge57, isMalaysianCitizen: true, AsOf));
    }

    [Fact]
    public void NonMalaysian55To59_IsScheme1_NoRegistrantAmbiguity()
    {
        Assert.Equal(
            SocsoScheme.EMPLOYMENT_INJURY_INVALIDITY,
            SocsoSchemeAdvisor.Recommend(DobAge57, isMalaysianCitizen: false, AsOf));
    }

    // Unknown nationality falls back to the conservative Malaysian rule.
    [Fact]
    public void UnknownNationality55To59_IsAmbiguous()
    {
        Assert.Null(SocsoSchemeAdvisor.Recommend(DobAge57, isMalaysianCitizen: null, AsOf));
    }

    [Fact]
    public void MissingDateOfBirth_RecommendsNothing()
    {
        Assert.Null(SocsoSchemeAdvisor.Recommend(null, isMalaysianCitizen: false, AsOf));
    }

    // NeedsManualChoice must line up exactly with Recommend returning null for an
    // age reason — a DOB-less employee gets no prompt, just an empty dropdown.
    [Theory]
    [InlineData(true, true)]     // Malaysian 55–59 → ask
    [InlineData(null, true)]     // unknown nationality → ask
    [InlineData(false, false)]   // foreign worker → auto-fills Scheme 1
    public void ManualChoice_IsOnlyNeededInThe55To59Window(bool? isMalaysian, bool expected)
    {
        Assert.Equal(
            expected,
            SocsoSchemeAdvisor.NeedsManualChoice(DobAge57, isMalaysian, AsOf));
    }

    [Fact]
    public void ManualChoice_NotNeededOutsideTheWindow()
    {
        Assert.False(SocsoSchemeAdvisor.NeedsManualChoice(DobAge40, true, AsOf));
        Assert.False(SocsoSchemeAdvisor.NeedsManualChoice(DobAge65, true, AsOf));
        Assert.False(SocsoSchemeAdvisor.NeedsManualChoice(null, true, AsOf));
    }

    // ─── CalculateAge ───────────────────────────────────────────────────

    [Fact]
    public void CalculateAge_CountsTheBirthdayOnTheDay()
    {
        var dob = new DateTime(1990, 6, 22);

        Assert.Equal(36, SocsoSchemeAdvisor.CalculateAge(dob, new DateTime(2026, 6, 22)));
        Assert.Equal(35, SocsoSchemeAdvisor.CalculateAge(dob, new DateTime(2026, 6, 21)));
    }

    [Fact]
    public void CalculateAge_ClampsAFutureDateOfBirthToZero()
    {
        Assert.Equal(0, SocsoSchemeAdvisor.CalculateAge(new DateTime(2030, 1, 1), AsOf));
    }
}
