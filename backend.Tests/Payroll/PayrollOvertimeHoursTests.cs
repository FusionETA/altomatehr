using AltomateHR.Api.Modules.Overtime;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;

namespace AltomateHR.Api.Tests.Payroll;

// Which overtime a payslip pays: approved requests by default, the admin's
// typed figure when there is one — never both.
public class PayrollOvertimeHoursTests
{
    private static PayrollRunAdjustment Typed(decimal normal = 0m, decimal rest = 0m, decimal ph = 0m) =>
        new() { OtNormalHours = normal, OtRestHours = rest, OtPublicHours = ph };

    [Fact]
    public void UsesApprovedOvertimeWhenNothingIsTyped()
    {
        var hours = PayrollOvertimeHours.Resolve(null, new ApprovedOvertimeMinutes(90, 120, 30));

        Assert.Equal(PayrollOvertimeHours.Source.Approved, hours.Source);
        Assert.Equal(1.5m, hours.Normal);
        Assert.Equal(2m, hours.Rest);
        Assert.Equal(0.5m, hours.PublicHoliday);
    }

    // A row that exists for other reasons, with OT left at zero, is "not typed".
    [Fact]
    public void TreatsAnAdjustmentWithZeroOtAsNotTyped()
    {
        var hours = PayrollOvertimeHours.Resolve(Typed(), new ApprovedOvertimeMinutes(60, 0, 0));

        Assert.Equal(PayrollOvertimeHours.Source.Approved, hours.Source);
        Assert.Equal(1m, hours.Normal);
    }

    // All three typed figures replace all three approved ones, even the buckets
    // the admin left at zero — a partial override would mix two sources.
    [Fact]
    public void TypedHoursReplaceEveryApprovedBucket()
    {
        var hours = PayrollOvertimeHours.Resolve(
            Typed(normal: 3m), new ApprovedOvertimeMinutes(600, 240, 60));

        Assert.Equal(PayrollOvertimeHours.Source.Adjustment, hours.Source);
        Assert.Equal(3m, hours.Normal);
        Assert.Equal(0m, hours.Rest);
        Assert.Equal(0m, hours.PublicHoliday);
    }

    // Hours for the company's own types come through, approved or typed —
    // and typed ones replace every approved hour, built-in or not.
    [Fact]
    public void CarriesTheCompanysOwnTypes()
    {
        var approved = PayrollOvertimeHours.Resolve(
            null, new ApprovedOvertimeMinutes(60, 0, 0, new Dictionary<string, int> { ["night"] = 90 }));
        var typed = PayrollOvertimeHours.Resolve(
            new PayrollRunAdjustment { OtCustomHoursJson = """{"night":2}""" },
            new ApprovedOvertimeMinutes(600, 0, 0, new Dictionary<string, int> { ["night"] = 90 }));

        Assert.Equal((1m, 1.5m), (approved.Normal, approved.Custom!["night"]));
        Assert.Equal(PayrollOvertimeHours.Source.Adjustment, typed.Source);
        Assert.Equal((0m, 2m), (typed.Normal, typed.Custom!["night"]));
    }

    [Fact]
    public void IsNothingWhenThereIsNeither()
    {
        var hours = PayrollOvertimeHours.Resolve(null, null);

        Assert.Equal(PayrollOvertimeHours.Source.None, hours.Source);
        Assert.False(hours.Any);
    }

    // Hours are stored and printed at 2dp; the payslip must read back to what
    // was paid, so the conversion rounds there (half away from zero).
    [Theory]
    [InlineData(50, 0.83)]
    [InlineData(45, 0.75)]
    [InlineData(1, 0.02)]
    public void ConvertsMinutesToHoursAtTwoDecimals(int minutes, double expected)
    {
        Assert.Equal((decimal)expected, PayrollOvertimeHours.ToHours(minutes));
    }
}
