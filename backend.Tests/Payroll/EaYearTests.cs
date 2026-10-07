using AltomateHR.Api.Modules.LhdnForms;
using AltomateHR.Api.Modules.Payroll;

namespace AltomateHR.Api.Tests.Payroll;

// When one employee's Form EA is final, and what the "not yet" line says.
// The service-level cases (leavers, late pay, pending runs) are in
// EmployeePayrollServiceTests; these pin the wording HR and the employee see.
public class EaYearTests
{
    private static readonly int[] JanToMar = [1, 2, 3];

    [Fact]
    public void AFullYearStillOpen_AsksForAllTwelveMonths()
    {
        var ea = EaYear.For(2026, leaveDate: null, lastPaidMonth: 3, JanToMar, hasUnsubmittedPay: false);

        Assert.False(ea.Ready);
        Assert.Equal(
            "The 2026 EA form will be ready once all 12 months of 2026 payroll are approved.",
            ea.NotReadyReason(2026));
    }

    [Fact]
    public void ALeaverWaitingOnTheirLeavingMonth_NamesThatMonth()
    {
        var ea = EaYear.For(2026, new DateTime(2026, 4, 10), lastPaidMonth: 3, JanToMar, hasUnsubmittedPay: false);

        Assert.False(ea.Ready);
        Assert.Equal(
            "The 2026 EA form will be ready once 2026 payroll is approved up to April.",
            ea.NotReadyReason(2026));
    }

    // Every month is approved, but a run still holds their pay — saying
    // "approved up to March" would read as already done.
    [Fact]
    public void PendingPay_SaysSoRatherThanNamingAnApprovedMonth()
    {
        var ea = EaYear.For(2026, new DateTime(2026, 3, 10), lastPaidMonth: 3, JanToMar, hasUnsubmittedPay: true);

        Assert.False(ea.Ready);
        Assert.Contains("payroll run still holding pay", ea.NotReadyReason(2026));
    }

    [Fact]
    public void TheAdminDownload_IsNamedByEmployeeAndYear()
    {
        Assert.Equal("EA_E-001_2025.pdf", LhdnFormMeta.FileName(LhdnFormKind.EA, "E-001", 2025));
        Assert.True(LhdnFormMeta.All[LhdnFormKind.EA].NeedsYearPicker);
    }
}
