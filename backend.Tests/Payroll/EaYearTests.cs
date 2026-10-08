using AltomateHR.Api.Modules.LhdnForms;
using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// When one employee's Form EA is final, and what the "not yet" line says.
// The service-level cases are in EmployeePayrollServiceTests.
public class EaYearTests
{
    [Fact]
    public void ElevenMonths_IsNotReady()
    {
        var ea = EaYear.For([.. Enumerable.Range(1, 11)]);

        Assert.False(ea.Ready);
        Assert.Equal(11, ea.ApprovedMonths);
        Assert.Equal(12, ea.RequiredMonths);
    }

    [Fact]
    public void TwelveMonths_IsReady()
    {
        var ea = EaYear.For([.. Enumerable.Range(1, 12)]);

        Assert.True(ea.Ready);
        Assert.Equal(12, ea.ApprovedMonths);
    }

    // Runs alone can't tell "started here in July" from "paid Jan–Jun in
    // another system and never imported it", so by default January is owed.
    [Fact]
    public void Runs_from_July_alone_are_not_the_whole_year()
    {
        var ea = EaYear.For([.. Enumerable.Range(7, 6)]);

        Assert.False(ea.Ready);
        Assert.Equal(12, ea.RequiredMonths);
        Assert.Equal([1, 2, 3, 4, 5, 6], EaYear.Missing([.. Enumerable.Range(7, 6)]));
    }

    // The admin said payroll here started in March / July (a new company, or
    // one staff were transferred into): those months through December are it.
    [Theory]
    [InlineData(3, 10)]
    [InlineData(7, 6)]
    public void A_company_set_as_starting_mid_year_is_ready_once_it_reaches_December(int start, int required)
    {
        var through = Enumerable.Range(start, 13 - start).ToList();

        var ea = EaYear.For(through, startMonth: start);

        Assert.True(ea.Ready);
        Assert.Equal(required, ea.RequiredMonths);
        Assert.Equal(start, ea.FirstMonth);
    }

    [Fact]
    public void It_waits_for_December()
    {
        int[] marToNov = [.. Enumerable.Range(3, 9)];

        Assert.False(EaYear.For(marToNov, startMonth: 3).Ready);
        Assert.Equal([12], EaYear.Missing(marToNov, startMonth: 3));
    }

    // An earlier run than the start (an import, or a draft) pulls the start
    // back: a draft January holds the year back even with July set.
    [Fact]
    public void An_earlier_draft_still_holds_the_year_back()
    {
        int[] approved = [.. Enumerable.Range(7, 6)];        // Jul–Dec
        int[] anyStatus = [1, .. approved];                   // plus a draft January

        Assert.True(EaYear.For(approved, runMonths: null, startMonth: 7).Ready);
        Assert.False(EaYear.For(approved, anyStatus, startMonth: 7).Ready);
        Assert.Equal([1, 2, 3, 4, 5, 6], EaYear.Missing(approved, anyStatus, startMonth: 7));
    }

    // The start year's setting says nothing about later years (January) and
    // means "no payroll here yet" for earlier ones.
    [Fact]
    public void The_start_month_applies_to_its_own_year()
    {
        var settings = new AltomateHR.Api.Modules.Payroll.Entities.PayrollSettings
        {
            PayrollStartYear = 2026, PayrollStartMonth = 7,
        };

        Assert.Equal(7, settings.YearEndStartMonth(2026));
        Assert.Equal(1, settings.YearEndStartMonth(2027));
        Assert.Equal(13, settings.YearEndStartMonth(2025));
        Assert.Equal(1, new AltomateHR.Api.Modules.Payroll.Entities.PayrollSettings().YearEndStartMonth(2026));
    }

    [Fact]
    public void No_runs_at_all_is_not_ready()
    {
        Assert.False(EaYear.For([]).Ready);
    }

    [Fact]
    public void TheReason_SaysThroughDecember()
    {
        Assert.Equal(
            "The 2026 EA form will be ready once this company's 2026 payroll is approved through December.",
            EaYear.NotReadyReason(2026));
    }

    [Fact]
    public void TheAdminDownload_IsNamedByEmployeeAndYear()
    {
        Assert.Equal("EA_E-001_2025.pdf", LhdnFormMeta.FileName(LhdnFormKind.EA, "E-001", 2025));
        Assert.True(LhdnFormMeta.All[LhdnFormKind.EA].NeedsYearPicker);
    }
}
