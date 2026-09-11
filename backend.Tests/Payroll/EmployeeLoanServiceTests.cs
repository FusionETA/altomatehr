using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth.Entities;
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

    private void SeedSubmittedRun(int year, int month)
    {
        _db.PayrollRuns.Add(new PayrollRun
        {
            Id = $"run-{year}-{month}",
            OrganizationId = "org-1",
            PeriodYear = year,
            PeriodMonth = month,
            Status = PayrollRunStatus.SUBMITTED,
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
}
