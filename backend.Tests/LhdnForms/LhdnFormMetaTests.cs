using AltomateHR.Api.Modules.LhdnForms;

namespace AltomateHR.Api.Tests.LhdnForms;

// Which per-employee LHDN forms can be generated, and when they are due.
//
// CP22A and CP21 are due at least 30 days BEFORE the last day, while the
// person is still active — so they open on the leave date, not on archiving
// (which HR does after the person has gone).
public class LhdnFormMetaTests
{
    private static readonly DateTime Today = new(2026, 9, 28);

    [Theory]
    [InlineData(LhdnFormKind.CP22A)]
    [InlineData(LhdnFormKind.CP21)]
    [InlineData(LhdnFormKind.TP3)]
    public void ALeaversForms_OpenOnceALeaveDateIsSet_BeforeArchiving(LhdnFormKind kind)
    {
        Assert.False(LhdnFormMeta.IsAvailable(kind, isArchived: false, leaveDate: null));
        Assert.True(LhdnFormMeta.IsAvailable(kind, isArchived: false, leaveDate: Today.AddDays(45)));
        Assert.True(LhdnFormMeta.IsAvailable(kind, isArchived: true, leaveDate: null));
    }

    [Fact]
    public void Cp22_StaysForActiveEmployees_EvenWithALeaveDate()
    {
        Assert.True(LhdnFormMeta.IsAvailable(LhdnFormKind.CP22, isArchived: false, leaveDate: Today.AddDays(45)));
        Assert.False(LhdnFormMeta.IsAvailable(LhdnFormKind.CP22, isArchived: true, leaveDate: null));
    }

    [Theory]
    [InlineData(60, "Due in 30 days", "success")]
    [InlineData(35, "Due in 5 day(s)", "pending")]
    [InlineData(30, "Due in 0 day(s)", "pending")]
    [InlineData(20, "Overdue by 10 days", "rejected")]
    [InlineData(0, "Overdue by 30 days", "rejected")]
    public void TheCessationNotice_IsDue30DaysBeforeTheLeaveDate(int daysToLeaving, string text, string variant)
    {
        var badge = LhdnFormMeta.CessationDeadlineBadge(Today.AddDays(daysToLeaving), Today);

        Assert.Equal((text, variant), badge);
    }

    [Fact]
    public void OnceThePersonHasLeft_ThereIsNoCountdown()
    {
        Assert.Null(LhdnFormMeta.CessationDeadlineBadge(Today.AddDays(-1), Today));
        Assert.Null(LhdnFormMeta.CessationDeadlineBadge(null, Today));
    }
}
