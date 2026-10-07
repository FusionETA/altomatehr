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
    }

    [Fact]
    public void TwelveMonths_IsReady()
    {
        var ea = EaYear.For([.. Enumerable.Range(1, 12)]);

        Assert.True(ea.Ready);
        Assert.Equal(12, ea.ApprovedMonths);
    }

    [Fact]
    public void TheReason_SaysAllTwelveMonths()
    {
        Assert.Equal(
            "The 2026 EA form will be ready once all 12 months of 2026 payroll are approved.",
            EaYear.NotReadyReason(2026));
    }

    [Fact]
    public void TheAdminDownload_IsNamedByEmployeeAndYear()
    {
        Assert.Equal("EA_E-001_2025.pdf", LhdnFormMeta.FileName(LhdnFormKind.EA, "E-001", 2025));
        Assert.True(LhdnFormMeta.All[LhdnFormKind.EA].NeedsYearPicker);
    }
}
