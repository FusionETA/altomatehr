using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AltomateHR.Api.Tests.Payroll;

// A generated draft freezes its inputs. Only adjustments and claim attachments
// used to mark one stale, so a profile edit, a loan or a deleted unpaid leave
// left the draft's payslips quietly wrong and still submittable. These pin the
// sweep that the previous system ran (markDraftsStaleForOrg).
public class PayrollDraftStalenessTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly PayrollDraftStaleness _staleness;

    public PayrollDraftStalenessTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"draft-staleness-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _staleness = new PayrollDraftStaleness(
            new PayrollRunRepository(_db), _currentUser, NullLogger<PayrollDraftStaleness>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private PayrollRun Run(int month, PayrollRunStatus status = PayrollRunStatus.DRAFT,
        bool generated = true, string org = "org-1")
    {
        var run = new PayrollRun
        {
            OrganizationId = org,
            PeriodYear = 2026,
            PeriodMonth = month,
            Status = status,
            GeneratedAt = generated ? new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc) : null,
        };
        _db.PayrollRuns.Add(run);
        return run;
    }

    private async Task<PayrollRun> Reload(string id) =>
        await _db.PayrollRuns.IgnoreQueryFilters().AsNoTracking().SingleAsync(r => r.Id == id);

    [Fact]
    public async Task MarkAll_FlagsEveryGeneratedDraft_AndNothingElse()
    {
        var aug = Run(8);
        var sep = Run(9);
        var submitted = Run(7, PayrollRunStatus.SUBMITTED);
        var pending = Run(6, PayrollRunStatus.PENDING_APPROVAL);
        var empty = Run(10, generated: false);
        await _db.SaveChangesAsync();

        await _staleness.MarkAllDraftsAsync();

        Assert.NotNull((await Reload(aug.Id)).LastMutatedAt);
        Assert.NotNull((await Reload(sep.Id)).LastMutatedAt);
        // Filed or awaiting approval: not a draft, not touched.
        Assert.Null((await Reload(submitted.Id)).LastMutatedAt);
        Assert.Null((await Reload(pending.Id)).LastMutatedAt);
        // Never generated: nothing to be behind, and a banner on it would lie.
        Assert.Null((await Reload(empty.Id)).LastMutatedAt);
    }

    [Fact]
    public async Task MarkCovering_FlagsOnlyTheMonthsTheChangeTouches()
    {
        var aug = Run(8);
        var sep = Run(9);
        var oct = Run(10);
        await _db.SaveChangesAsync();

        // Unpaid leave 30 Sep – 2 Oct.
        await _staleness.MarkDraftsCoveringAsync(
            new DateTime(2026, 9, 30), new DateTime(2026, 10, 2));

        Assert.Null((await Reload(aug.Id)).LastMutatedAt);
        Assert.NotNull((await Reload(sep.Id)).LastMutatedAt);
        Assert.NotNull((await Reload(oct.Id)).LastMutatedAt);
    }

    [Fact]
    public async Task MarkCovering_AlsoStampsThatMonthsPendingAndSubmittedRuns()
    {
        var pending = Run(9, PayrollRunStatus.PENDING_APPROVAL);
        var submitted = Run(10, PayrollRunStatus.SUBMITTED);
        var otherMonth = Run(8, PayrollRunStatus.SUBMITTED);
        await _db.SaveChangesAsync();
        var submittedUpdatedAt = (await Reload(submitted.Id)).UpdatedAt;

        // Unpaid leave 30 Sep – 2 Oct cancelled.
        await _staleness.MarkDraftsCoveringAsync(
            new DateTime(2026, 9, 30), new DateTime(2026, 10, 2));

        // Stamped, so a send-back or revert to draft comes back needing a re-run.
        Assert.NotNull((await Reload(pending.Id)).LastMutatedAt);
        Assert.NotNull((await Reload(submitted.Id)).LastMutatedAt);
        // A filed run's own record didn't change.
        Assert.Equal(submittedUpdatedAt, (await Reload(submitted.Id)).UpdatedAt);
        Assert.Null((await Reload(otherMonth.Id)).LastMutatedAt);
    }

    [Fact]
    public async Task NeverTouchesAnotherCompanysDrafts()
    {
        var mine = Run(9);
        var theirs = Run(9, org: "org-2");
        await _db.SaveChangesAsync();

        await _staleness.MarkAllDraftsAsync();

        Assert.NotNull((await Reload(mine.Id)).LastMutatedAt);
        Assert.Null((await Reload(theirs.Id)).LastMutatedAt);
    }

    // Without a current org the tenant filter is off; a background job must
    // not sweep every company's drafts.
    [Fact]
    public async Task DoesNothingWithoutACurrentOrg()
    {
        var run = Run(9);
        await _db.SaveChangesAsync();
        _currentUser.OrganizationId = null;

        await _staleness.MarkAllDraftsAsync();

        Assert.Null((await Reload(run.Id)).LastMutatedAt);
    }

    [Fact]
    public void MonthsBetween_SpansYearEnds_AndAcceptsEitherOrder()
    {
        Assert.Equal(
            [(2026, 12), (2027, 1)],
            PayrollDraftStaleness.MonthsBetween(new DateTime(2027, 1, 3), new DateTime(2026, 12, 30)));
        Assert.Equal([(2026, 9)],
            PayrollDraftStaleness.MonthsBetween(new DateTime(2026, 9, 28), new DateTime(2026, 9, 28)));
    }
}
