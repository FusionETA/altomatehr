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

    // A company that started running payroll here in March (or one an
    // employee was transferred into in July) owes no January run.
    [Theory]
    [InlineData(3, 10)]
    [InlineData(7, 6)]
    public void A_company_that_started_mid_year_is_ready_once_it_reaches_December(int first, int required)
    {
        var through = Enumerable.Range(first, 13 - first).ToList();

        var ea = EaYear.For(through);

        Assert.True(ea.Ready);
        Assert.Equal(required, ea.RequiredMonths);
        Assert.Equal(first, ea.FirstMonth);
    }

    [Fact]
    public void It_waits_for_December()
    {
        var ea = EaYear.For([.. Enumerable.Range(3, 9)]);   // Mar–Nov

        Assert.False(ea.Ready);
        Assert.Equal([12], EaYear.Missing([.. Enumerable.Range(3, 9)]));
    }

    // A draft still holds the year back: a draft January makes January the
    // start, so the approved Feb–Dec are not enough.
    [Fact]
    public void A_draft_month_still_holds_the_year_back()
    {
        int[] approved = [.. Enumerable.Range(2, 11)];       // Feb–Dec
        int[] anyStatus = [.. Enumerable.Range(1, 12)];      // Jan is a draft

        Assert.False(EaYear.For(approved, anyStatus).Ready);
        Assert.Equal([1], EaYear.Missing(approved, anyStatus));
        Assert.True(EaYear.For(approved).Ready);              // without the draft, Feb is the start
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
