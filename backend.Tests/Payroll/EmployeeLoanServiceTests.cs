using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// Staff loans against a real EF context.
//
// The arithmetic is covered by PayrollLoansTests. What is pinned here is the
// behaviour around a loan that has STARTED repaying — the point after which
// changing it would restate months already filed.
public class EmployeeLoanServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly EmployeeLoanService _service;

    public EmployeeLoanServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-loans-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        _service = new EmployeeLoanService(
            new EmployeeLoanRepository(_db),
            new PayrollRunRepository(_db),
            TestDirectory.Over(
                new OrganizationMembershipRepository(_db),
                new UserRepository(_db),
                new EmployeeProfileRepository(_db)),
            _audit);

        Seed();
    }

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        _db.Users.Add(new User { Id = "usr-1", Email = "aisyah@x.com", Name = "Aisyah Binti Rahman" });
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "emp-1",
            OrganizationId = "org-1",
            UserId = "usr-1",
        });
        _db.SaveChanges();
    }

    private void SeedSubmittedRun(int year, int month) =>
        SeedRun(year, month, PayrollRunStatus.SUBMITTED);

    private void SeedRun(int year, int month, PayrollRunStatus status)
    {
        _db.PayrollRuns.Add(new PayrollRun
        {
            Id = $"run-{year}-{month}",
            OrganizationId = "org-1",
            PeriodYear = year,
            PeriodMonth = month,
            Status = status,
        });
        _db.SaveChanges();
    }

    private static SaveEmployeeLoanDto Save(
        decimal principal = 1200m,
        LoanRepaymentMode mode = LoanRepaymentMode.FIXED,
        int? count = 12,
        decimal? amount = null,
        int startYear = 2026,
        int startMonth = 1,
        IReadOnlyList<decimal>? schedule = null) => new()
        {
            EmployeeProfileId = "emp-1",
            PrincipalAmount = principal,
            Mode = mode,
            InstallmentCount = count,
            InstallmentAmount = amount,
            StartYear = startYear,
            StartMonth = startMonth,
            Schedule = schedule,
        };

    // ─── Creating ───────────────────────────────────────────────────────

    [Fact]
    public async Task ANewLoan_GetsAScheduleAndAName()
    {
        var loan = await _service.CreateAsync(Save());

        Assert.Equal(12, loan.InstallmentCount);
        Assert.Equal(100m, loan.InstallmentAmount);
        Assert.Equal(12, loan.Schedule.Count);
        Assert.Equal(1200m, loan.Schedule.Sum(i => i.Amount));
        Assert.Equal("Aisyah Binti Rahman", loan.EmployeeName);
        Assert.Equal("Jan 2026", loan.Schedule[0].PeriodLabel);
    }

    [Fact]
    public async Task AHandVariedSchedule_IsKeptVerbatim()
    {
        var loan = await _service.CreateAsync(Save(
            principal: 1200m, schedule: [100m, 100m, 1000m]));

        Assert.Equal(3, loan.InstallmentCount);
        Assert.Equal([100m, 100m, 1000m], loan.Schedule.Select(i => i.Amount));
    }

    [Fact]
    public async Task AScheduleThatDoesNotAddUp_IsRefused()
    {
        await Assert.ThrowsAsync<PayrollLoanException>(() =>
            _service.CreateAsync(Save(principal: 1200m, schedule: [100m, 100m])));
    }

    [Fact]
    public async Task CreatingIsAudited()
    {
        await _service.CreateAsync(Save());

        Assert.True(_audit.Recorded("payroll.loan.create"));
    }

    // ─── A loan that has started is fixed ───────────────────────────────

    // Its earlier installments are inside filed payslips. Re-terming it would
    // leave those months deducting an amount the schedule no longer contains.
    [Fact]
    public async Task ALoanThatHasStarted_CannotBeReTermed()
    {
        var created = await _service.CreateAsync(Save());
        SeedSubmittedRun(2026, 1);

        var ex = await Assert.ThrowsAsync<PayrollLoanException>(() =>
            _service.UpdateAsync(created.Id, Save(principal: 2400m)));

        Assert.Contains("already started repaying", ex.Message);
    }

    [Fact]
    public async Task ALoanThatHasStarted_CannotBeDeleted()
    {
        var created = await _service.CreateAsync(Save());
        SeedSubmittedRun(2026, 1);

        var ex = await Assert.ThrowsAsync<PayrollLoanException>(() =>
            _service.DeleteAsync(created.Id));

        Assert.Contains("Cancel it instead", ex.Message);
    }

    // Before it starts, it is just a plan — editing and deleting are free.
    [Fact]
    public async Task ALoanThatHasNotStarted_CanBeEditedAndDeleted()
    {
        var created = await _service.CreateAsync(Save());

        var updated = await _service.UpdateAsync(created.Id, Save(principal: 2400m, count: 6));
        Assert.Equal(2400m, updated!.PrincipalAmount);
        Assert.Equal(6, updated.InstallmentCount);

        Assert.True(await _service.DeleteAsync(created.Id));
        Assert.Empty(await _service.GetAllAsync());
    }

    // A DRAFT run is not evidence of anything being deducted yet.
    [Fact]
    public async Task ADraftRun_DoesNotStartALoan()
    {
        var created = await _service.CreateAsync(Save());
        _db.PayrollRuns.Add(new PayrollRun
        {
            Id = "run-draft",
            OrganizationId = "org-1",
            PeriodYear = 2026,
            PeriodMonth = 1,
            Status = PayrollRunStatus.DRAFT,
        });
        await _db.SaveChangesAsync();

        Assert.False((await _service.GetAsync(created.Id))!.HasStarted);
    }

    // ─── Progress tracks the submitted runs ─────────────────────────────

    [Fact]
    public async Task ProgressFollowsTheSubmittedRuns()
    {
        var created = await _service.CreateAsync(Save());
        SeedSubmittedRun(2026, 1);
        SeedSubmittedRun(2026, 2);

        var loan = await _service.GetAsync(created.Id);

        Assert.Equal(2, loan!.PaidInstallments);
        Assert.Equal(200m, loan.PaidAmount);
        Assert.Equal(1000m, loan.RemainingAmount);
        Assert.True(loan.Schedule[0].Paid);
        Assert.False(loan.Schedule[2].Paid);
    }

    // ─── What a run deducts ─────────────────────────────────────────────

    [Fact]
    public async Task TheRepaymentForAPeriod_IsTheInstallmentDue()
    {
        await _service.CreateAsync(Save());

        var due = await _service.GetRepaymentsForPeriodAsync(2026, 1);

        Assert.Equal(100m, due["emp-1"]);
    }

    // Someone with two loans repays both in the same month.
    [Fact]
    public async Task TwoLoansForOnePerson_AreSummed()
    {
        await _service.CreateAsync(Save(principal: 1200m, count: 12));
        await _service.CreateAsync(Save(principal: 600m, count: 6));

        var due = await _service.GetRepaymentsForPeriodAsync(2026, 1);

        Assert.Equal(200m, due["emp-1"]);
    }

    // Cancelling has to stop the deduction from the very next run.
    [Fact]
    public async Task ACancelledLoan_StopsDeducting()
    {
        var created = await _service.CreateAsync(Save());
        await _service.SetStatusAsync(created.Id, LoanStatus.CANCELLED);

        Assert.Empty(await _service.GetRepaymentsForPeriodAsync(2026, 1));
    }

    [Fact]
    public async Task APeriodBeforeTheLoanStarts_DeductsNothing()
    {
        await _service.CreateAsync(Save(startYear: 2026, startMonth: 6));

        Assert.Empty(await _service.GetRepaymentsForPeriodAsync(2026, 1));
    }

    // ─── Tenant isolation ───────────────────────────────────────────────

    [Fact]
    public async Task AnotherOrgSeesNoneOfOurLoans()
    {
        await _service.CreateAsync(Save());

        _currentUser.OrganizationId = "org-2";
        Assert.Empty(await _service.GetAllAsync());
        Assert.Empty(await _service.GetRepaymentsForPeriodAsync(2026, 1));

        _currentUser.OrganizationId = "org-1";
        Assert.Single(await _service.GetAllAsync());
    }

    [Fact]
    public async Task AMissingLoan_IsNotFound()
    {
        Assert.Null(await _service.GetAsync("nope"));
        Assert.Null(await _service.UpdateAsync("nope", Save()));
        Assert.False(await _service.DeleteAsync("nope"));
    }

    // ─── Changing a loan that has started ───────────────────────────────

    // RM 1,200 over 12 months from Jan 2026 (RM 100 a month), Jan–Mar filed.
    private async Task<EmployeeLoanDto> StartedLoanAsync()
    {
        var created = await _service.CreateAsync(Save());
        SeedSubmittedRun(2026, 1);
        SeedSubmittedRun(2026, 2);
        SeedSubmittedRun(2026, 3);
        return (await _service.GetAsync(created.Id))!;
    }

    [Fact]
    public async Task AStartedLoan_ShowsWhatIsLeftToPlan()
    {
        var loan = await StartedLoanAsync();

        Assert.Equal(2026, loan.FirstEditableYear);
        Assert.Equal(4, loan.FirstEditableMonth);
        Assert.Equal(900m, loan.RemainingToPlan);
        Assert.All(loan.Schedule.Take(3), i => Assert.True(i.Locked));
    }

    // Longer: the RM 900 left over 18 months instead of 9. The filed months
    // stay, the end moves out, and "over N months" no longer describes it.
    [Fact]
    public async Task ReplanningAStartedLoan_KeepsTheFiledMonthsAndMakesItCustom()
    {
        var loan = await StartedLoanAsync();

        var replanned = (await _service.ReplanAsync(loan.Id, new ReplanLoanDto
        {
            Mode = LoanRepaymentMode.FIXED,
            InstallmentCount = 18,
        }))!;

        Assert.Equal(LoanRepaymentMode.CUSTOM, replanned.Mode);
        Assert.Equal(21, replanned.InstallmentCount);
        Assert.Equal([100m, 100m, 100m], replanned.Schedule.Take(3).Select(i => i.Amount));
        Assert.Equal(50m, replanned.InstallmentAmount);
        Assert.Equal(3, replanned.PaidInstallments);
        Assert.Equal((2027, 9), (replanned.EndYear, replanned.EndMonth));
        Assert.Equal(1200m, replanned.Schedule.Sum(i => i.Amount));

        var audit = _audit.Written.Single(e => e.Action == AuditActions.PayrollLoanReplan);
        Assert.Contains("900.00", audit.Summary);
    }

    // An approver is looking at April's payslips, loan deduction included —
    // it must not move under them.
    [Fact]
    public async Task AMonthAwaitingApproval_IsLockedToo()
    {
        var created = await _service.CreateAsync(Save());
        SeedRun(2026, 1, PayrollRunStatus.PENDING_APPROVAL);

        var replanned = (await _service.ReplanAsync(created.Id, new ReplanLoanDto
        {
            Mode = LoanRepaymentMode.FIXED,
            InstallmentCount = 2,
        }))!;

        Assert.Equal([100m, 550m, 550m], replanned.Schedule.Select(i => i.Amount));
    }

    // The old "cancel and record a new one" dead end now points to Re-plan.
    [Fact]
    public async Task EditingAStartedLoanWholesale_PointsToReplan()
    {
        var loan = await StartedLoanAsync();

        var ex = await Assert.ThrowsAsync<PayrollLoanException>(() =>
            _service.UpdateAsync(loan.Id, Save(principal: 2400m)));

        Assert.Contains("Re-plan", ex.Message);
    }

    [Fact]
    public async Task SkippingMonths_PushesTheLoanLater()
    {
        var loan = await StartedLoanAsync();

        var skipped = (await _service.SkipMonthsAsync(loan.Id, new SkipLoanMonthsDto
        {
            FromYear = 2026, FromMonth = 5, Months = 2,
        }))!;

        Assert.Equal(0m, await RepaymentAsync(2026, 5));
        Assert.Equal(0m, await RepaymentAsync(2026, 6));
        Assert.Equal(100m, await RepaymentAsync(2026, 7));
        Assert.Equal((2027, 2), (skipped.EndYear, skipped.EndMonth));
        Assert.Equal(LoanRepaymentMode.CUSTOM, skipped.Mode);
    }

    [Fact]
    public async Task AFiledMonth_CannotBeSkipped()
    {
        var loan = await StartedLoanAsync();

        var ex = await Assert.ThrowsAsync<PayrollLoanException>(() =>
            _service.SkipMonthsAsync(loan.Id, new SkipLoanMonthsDto
            {
                FromYear = 2026, FromMonth = 2, Months = 1,
            }));

        Assert.Contains("Apr 2026", ex.Message);   // the earliest that can change
    }

    // Paused from April, April and May run while paused, resumed from June:
    // those two months become RM 0 and the loan ends two months later.
    [Fact]
    public async Task PausingThenResuming_WritesThePauseInAndEndsLater()
    {
        var loan = await StartedLoanAsync();

        var paused = (await _service.PauseAsync(loan.Id, new PauseLoanDto { FromYear = 2026, FromMonth = 4 }))!;
        Assert.Equal(LoanStatus.PAUSED, paused.Status);
        Assert.Equal(100m, await RepaymentAsync(2026, 3));
        Assert.Equal(0m, await RepaymentAsync(2026, 4));

        SeedSubmittedRun(2026, 4);
        SeedSubmittedRun(2026, 5);
        var whilePaused = (await _service.GetAsync(loan.Id))!;
        Assert.Equal(3, whilePaused.PaidInstallments);   // April and May took nothing

        var resumed = (await _service.ResumeAsync(loan.Id, new ResumeLoanDto { Year = 2026, Month = 6 }))!;

        Assert.Equal(LoanStatus.ACTIVE, resumed.Status);
        Assert.Null(resumed.PausedFromYear);
        Assert.Equal(0m, resumed.Schedule[3].Amount);
        Assert.Equal(0m, resumed.Schedule[4].Amount);
        Assert.Equal(100m, await RepaymentAsync(2026, 6));
        Assert.Equal((2027, 2), (resumed.EndYear, resumed.EndMonth));
        Assert.Equal(900m, resumed.RemainingAmount);
    }

    [Fact]
    public async Task ResumingIntoAFiledMonth_IsRefused()
    {
        var loan = await StartedLoanAsync();
        await _service.PauseAsync(loan.Id, new PauseLoanDto { FromYear = 2026, FromMonth = 4 });
        SeedSubmittedRun(2026, 4);

        var ex = await Assert.ThrowsAsync<PayrollLoanException>(() =>
            _service.ResumeAsync(loan.Id, new ResumeLoanDto { Year = 2026, Month = 4 }));

        Assert.Contains("May 2026", ex.Message);
    }

    // "Reactivate" would silently resume from whenever — Resume asks which month.
    [Fact]
    public async Task APausedLoan_IsResumedNotReactivated()
    {
        var loan = await StartedLoanAsync();
        await _service.PauseAsync(loan.Id, new PauseLoanDto { FromYear = 2026, FromMonth = 4 });

        await Assert.ThrowsAsync<PayrollLoanException>(() =>
            _service.SetStatusAsync(loan.Id, LoanStatus.ACTIVE));
    }

    // Cancelled while paused: the paused months already filed took nothing,
    // so they are written in as RM 0 rather than read back as repaid.
    [Fact]
    public async Task CancellingAPausedLoan_DoesNotCountThePausedMonthsAsRepaid()
    {
        var loan = await StartedLoanAsync();
        await _service.PauseAsync(loan.Id, new PauseLoanDto { FromYear = 2026, FromMonth = 4 });
        SeedSubmittedRun(2026, 4);

        var cancelled = (await _service.SetStatusAsync(loan.Id, LoanStatus.CANCELLED))!;

        Assert.Equal(300m, cancelled.PaidAmount);
        Assert.Equal(0m, cancelled.Schedule[3].Amount);
        Assert.Equal(900m, cancelled.RemainingAmount);
    }

    [Fact]
    public async Task APausedLoan_CannotBeReplannedUntilResumed()
    {
        var loan = await StartedLoanAsync();
        await _service.PauseAsync(loan.Id, new PauseLoanDto { FromYear = 2026, FromMonth = 4 });

        var ex = await Assert.ThrowsAsync<PayrollLoanException>(() =>
            _service.ReplanAsync(loan.Id, new ReplanLoanDto { InstallmentCount = 3 }));

        Assert.Contains("Resume", ex.Message);
    }

    // The payroll roster lists members without a saved profile under a
    // stand-in id. A loan against one would never deduct.
    [Fact]
    public async Task ALoanForSomeoneWithNoPayrollProfile_IsRefused()
    {
        var dto = Save();
        dto.EmployeeProfileId = "stand-in-guid";

        var ex = await Assert.ThrowsAsync<PayrollLoanException>(() => _service.CreateAsync(dto));

        Assert.Contains("no payroll profile", ex.Message);
    }

    private async Task<decimal> RepaymentAsync(int year, int month) =>
        (await _service.GetRepaymentsForPeriodAsync(year, month)).GetValueOrDefault("emp-1");
}
