using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// The salary-change trail, end to end.
//
// The arithmetic is covered by SalaryChangeHintsTests. What is pinned here is
// that editing an employee's salary ACTUALLY WRITES the history — a trail
// nothing populates is worse than no trail, because it reads as "this person
// has never had a raise".
public class SalaryChangeServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new() { UserId = "usr-admin" };
    private readonly FakeAuditService _audit = new();
    private readonly SalaryChangeService _service;
    private readonly EmployeeProfileService _profiles;

    public SalaryChangeServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"salary-change-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new UserRepository(_db),
            new EmployeeProfileRepository(_db));

        _service = new SalaryChangeService(
            new SalaryChangeRepository(_db),
            new PayrollRunRepository(_db),
            new PayslipRepository(_db),
            new PayrollRunAdjustmentRepository(_db),
            new PayrollSettingsService(new PayrollSettingsRepository(_db), _audit),
            directory,
            _currentUser,
            _audit);

        // The real profile service, so the hook that records a change is
        // exercised rather than the service being called directly.
        _profiles = new EmployeeProfileService(
            new EmployeeProfileRepository(_db),
            new OrganizationMembershipRepository(_db),
            directory,
            _service);

        Seed();
    }

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        _db.Users.Add(new User { Id = "usr-1", Email = "aisyah@x.com", Name = "Aisyah Binti Rahman" });
        _db.Users.Add(new User { Id = "usr-admin", Email = "admin@x.com", Name = "The Admin" });
        _db.OrganizationMemberships.Add(new OrganizationMembership
        {
            Id = "mem-1", OrganizationId = "org-1", UserId = "usr-1", Role = "Employee",
        });
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "emp-1",
            OrganizationId = "org-1",
            UserId = "usr-1",
            SalaryType = SalaryType.MONTHLY,
            MonthlySalary = 5000m,
        });
        _db.SaveChanges();
    }

    private static EmployeeProfileDto Edit(
        decimal? monthly = 6000m,
        SalaryType type = SalaryType.MONTHLY,
        DateTime? effective = null,
        SalaryChangeReason? reason = null,
        string? notes = null,
        string? phone = null) => new()
        {
            SalaryType = type,
            MonthlySalary = monthly,
            Phone = phone,
            SalaryChangeEffectiveDate = effective,
            SalaryChangeReason = reason,
            SalaryChangeNotes = notes,
        };

    // ─── Recording ──────────────────────────────────────────────────────

    [Fact]
    public async Task ASalaryEdit_WritesTheTrail()
    {
        await _profiles.SaveAsync("usr-1", Edit(
            monthly: 6000m,
            effective: new DateTime(2026, 1, 15),
            reason: SalaryChangeReason.PROMOTION,
            notes: "Promoted to senior"));

        var history = await _service.GetForEmployeeAsync("emp-1");
        var change = Assert.Single(history);

        Assert.Equal(5000m, change.PreviousMonthlySalary);
        Assert.Equal(6000m, change.NewMonthlySalary);
        Assert.Equal(new DateTime(2026, 1, 15), change.EffectiveDate);
        Assert.Equal(SalaryChangeReason.PROMOTION, change.Reason);
        Assert.Equal("Promoted to senior", change.Notes);
        Assert.Equal(20m, change.RaisePercent);
        // Who recorded it, not whose salary it is.
        Assert.Equal("The Admin", change.ChangedByName);
    }

    // An edit that touches something else must not manufacture a raise.
    [Fact]
    public async Task AnEditThatLeavesTheSalaryAlone_WritesNothing()
    {
        await _profiles.SaveAsync("usr-1", Edit(monthly: 5000m, phone: "012-3456789"));

        Assert.Empty(await _service.GetForEmployeeAsync("emp-1"));
    }

    [Fact]
    public async Task ASalaryTypeSwitch_IsStillRecorded()
    {
        await _profiles.SaveAsync("usr-1", Edit(monthly: null, type: SalaryType.HOURLY));

        var change = Assert.Single(await _service.GetForEmployeeAsync("emp-1"));

        Assert.Equal(SalaryType.MONTHLY, change.PreviousSalaryType);
        Assert.Equal(SalaryType.HOURLY, change.NewSalaryType);
        // A percentage across a type switch means nothing.
        Assert.Null(change.RaisePercent);
    }

    [Fact]
    public async Task WithNoEffectiveDateGiven_ItTakesEffectToday()
    {
        await _profiles.SaveAsync("usr-1", Edit());

        var change = Assert.Single(await _service.GetForEmployeeAsync("emp-1"));

        Assert.Equal(DateTime.UtcNow.Date, change.EffectiveDate);
    }

    [Fact]
    public async Task TheHistoryIsNewestFirst()
    {
        await _profiles.SaveAsync("usr-1", Edit(6000m, effective: new DateTime(2026, 1, 1)));
        await _profiles.SaveAsync("usr-1", Edit(7000m, effective: new DateTime(2026, 6, 1)));

        var history = await _service.GetForEmployeeAsync("emp-1");

        Assert.Equal(2, history.Count);
        Assert.Equal(new DateTime(2026, 6, 1), history[0].EffectiveDate);
        // Each row carries its OWN before, so the chain reads correctly even
        // though the profile has moved on twice.
        Assert.Equal(6000m, history[0].PreviousMonthlySalary);
        Assert.Equal(7000m, history[0].NewMonthlySalary);
        Assert.Equal(5000m, history[1].PreviousMonthlySalary);
    }

    [Fact]
    public async Task RecordingIsAudited()
    {
        await _profiles.SaveAsync("usr-1", Edit());

        Assert.True(_audit.Recorded("payroll.salary-change"));
    }

    // ─── Hints on a run ─────────────────────────────────────────────────

    private async Task SeedRunAsync(decimal snapshotSalary, int month = 1)
    {
        _db.PayrollRuns.Add(new PayrollRun
        {
            Id = $"run-{month}",
            OrganizationId = "org-1",
            PeriodYear = 2026,
            PeriodMonth = month,
            Status = PayrollRunStatus.DRAFT,
        });
        _db.Payslips.Add(new Payslip
        {
            Id = $"slip-{month}",
            OrganizationId = "org-1",
            PayrollRunId = $"run-{month}",
            EmployeeProfileId = "emp-1",
            UserId = "usr-1",
            SnapshotName = "Aisyah Binti Rahman",
            SnapshotEmployeeNumber = "E-001",
            SnapshotMonthlySalary = snapshotSalary,
            GrossPay = snapshotSalary,
            NetPay = snapshotSalary,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task AMidCycleRaise_SurfacesOnTheRunThatPaidIt()
    {
        await _profiles.SaveAsync("usr-1", Edit(6000m, effective: new DateTime(2026, 1, 15)));
        await SeedRunAsync(snapshotSalary: 6000m);

        var hint = Assert.Single(await _service.GetHintsForRunAsync("run-1"));

        Assert.Equal(SalaryChangeHints.Scenario.OVERPAID, hint.Outcome);
        Assert.Equal("Aisyah Binti Rahman", hint.EmployeeName);

        // The org has no payroll settings saved, so the divisor is the
        // TWENTY_SIX default rather than January's 31 calendar days:
        // 1,000 × 14 ÷ 26 = 538.46. The 14-day SPLIT is still calendar.
        Assert.Equal(WorkingDaysRule.TWENTY_SIX, hint.ProrationRule);
        Assert.Equal(14, hint.DaysAtOldRate);
        Assert.Equal(538.46m, hint.Delta);
    }

    // A change in a different month is not this run's problem.
    [Fact]
    public async Task AChangeInAnotherMonth_DoesNotSurface()
    {
        await _profiles.SaveAsync("usr-1", Edit(6000m, effective: new DateTime(2026, 3, 15)));
        await SeedRunAsync(snapshotSalary: 6000m);

        Assert.Empty(await _service.GetHintsForRunAsync("run-1"));
    }

    // Effective on the 1st needs no correction, so it is not worth a banner.
    [Fact]
    public async Task AChangeOnTheFirst_DoesNotSurface()
    {
        await _profiles.SaveAsync("usr-1", Edit(6000m, effective: new DateTime(2026, 1, 1)));
        await SeedRunAsync(snapshotSalary: 6000m);

        Assert.Empty(await _service.GetHintsForRunAsync("run-1"));
    }

    // Once the suggested line is on the run, the hint stops asking — the
    // marker in its label is how it knows.
    [Fact]
    public async Task AnAppliedCorrection_StopsBeingSuggested()
    {
        await _profiles.SaveAsync("usr-1", Edit(6000m, effective: new DateTime(2026, 1, 15)));
        await SeedRunAsync(snapshotSalary: 6000m);

        var before = Assert.Single(await _service.GetHintsForRunAsync("run-1"));
        Assert.NotNull(before.SuggestedLineItem);

        _db.PayrollRunAdjustments.Add(new PayrollRunAdjustment
        {
            OrganizationId = "org-1",
            PayrollRunId = "run-1",
            EmployeeProfileId = "emp-1",
            ManualLineItemsJson = System.Text.Json.JsonSerializer.Serialize(new[]
            {
                new
                {
                    kind = "DEDUCTION",
                    category = "deduct_salary_adjustment",
                    label = before.SuggestedLineItem!.Label,
                    amount = 538.46m,
                },
            }),
            FixedAllowanceOverridesJson = "{}",
        });
        await _db.SaveChangesAsync();

        var after = Assert.Single(await _service.GetHintsForRunAsync("run-1"));

        Assert.True(after.AlreadyApplied);
        Assert.Null(after.SuggestedLineItem);
    }

    [Fact]
    public async Task ARunWithNoPayslips_HasNoHints()
    {
        await _profiles.SaveAsync("usr-1", Edit(6000m, effective: new DateTime(2026, 1, 15)));
        _db.PayrollRuns.Add(new PayrollRun
        {
            Id = "run-empty", OrganizationId = "org-1",
            PeriodYear = 2026, PeriodMonth = 1, Status = PayrollRunStatus.DRAFT,
        });
        await _db.SaveChangesAsync();

        Assert.Empty(await _service.GetHintsForRunAsync("run-empty"));
    }

    [Fact]
    public async Task AMissingRun_HasNoHints()
    {
        Assert.Empty(await _service.GetHintsForRunAsync("nope"));
    }

    // ─── Tenant isolation ───────────────────────────────────────────────

    [Fact]
    public async Task AnotherOrgSeesNoneOfOurHistory()
    {
        await _profiles.SaveAsync("usr-1", Edit());

        _currentUser.OrganizationId = "org-2";
        Assert.Empty(await _service.GetForEmployeeAsync("emp-1"));

        _currentUser.OrganizationId = "org-1";
        Assert.Single(await _service.GetForEmployeeAsync("emp-1"));
    }
}
