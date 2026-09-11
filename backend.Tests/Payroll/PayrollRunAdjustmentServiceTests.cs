using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using AltomateHR.Api.Modules.Policies;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// Saving and clearing the per-(run, employee) adjustment row, against a real EF
// context so the tenant filter and the unique (run, employee) index run for
// real.
public class PayrollRunAdjustmentServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly PayrollRunAdjustmentService _service;
    private readonly PayrollRunRepository _runs;

    public PayrollRunAdjustmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-adjustments-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _runs = new PayrollRunRepository(_db);

        // The context read needs the roster, the policy (the overtime gate)
        // and the loan schedule, all over the same in-memory context so the
        // editor sees exactly what generation would.
        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new UserRepository(_db),
            new EmployeeProfileRepository(_db));

        _service = new PayrollRunAdjustmentService(
            new PayrollRunAdjustmentRepository(_db),
            _runs,
            directory,
            new PolicyService(
                new EmployeePolicyRepository(_db),
                new PolicyLeaveEntitlementRepository(_db),
                directory),
            new StubPayrollHours(),
            new EmployeeLoanService(
                new EmployeeLoanRepository(_db),
                new PayrollRunRepository(_db),
                directory,
                _audit),
            _audit);
    }

    public void Dispose() => _db.Dispose();

    // ─── Fixtures ───────────────────────────────────────────────────────

    private async Task<PayrollRun> AddRunAsync(
        PayrollRunStatus status = PayrollRunStatus.DRAFT, string organizationId = "org-1")
    {
        var run = new PayrollRun
        {
            OrganizationId = organizationId,
            PeriodYear = 2026,
            PeriodMonth = 1,
            Status = status,
            GeneratedAt = new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc),
        };
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    private static SavePayrollRunAdjustmentDto Dto(
        decimal otNormal = 0m,
        List<ManualLineItemDto>? items = null,
        Dictionary<string, FixedAllowanceOverrideDto>? overrides = null,
        string? notes = null) => new()
    {
        OtNormalHours = otNormal,
        ManualLineItems = items ?? [],
        FixedAllowanceOverrides = overrides ?? [],
        Notes = notes,
    };

    // ─── Saving ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_CreatesTheRowOnADraftRun()
    {
        var run = await AddRunAsync();

        var result = await _service.SaveAsync(run.Id, "emp-1", Dto(otNormal: 7.5m, notes: " late nights "));

        Assert.True(result.Ok);
        Assert.Equal(7.5m, result.Adjustment!.OtNormalHours);
        Assert.Equal("late nights", result.Adjustment.Notes);
        Assert.True(_audit.Recorded(AuditActions.PayrollRunAdjustmentSave));
    }

    // The whole reason the run carries LastMutatedAt: the payslips on screen
    // were built from the inputs as they were, so an admin has to be told they
    // are now behind.
    [Fact]
    public async Task SaveAsync_MarksTheRunStale()
    {
        var run = await AddRunAsync();
        Assert.Null(run.LastMutatedAt);

        await _service.SaveAsync(run.Id, "emp-1", Dto(otNormal: 3m));

        var after = await _runs.GetByIdAsync(run.Id);
        Assert.NotNull(after!.LastMutatedAt);
        Assert.True(after.LastMutatedAt > after.GeneratedAt);
    }

    // A save REPLACES the row. A second save that omits a line item means the
    // admin deleted it, not that they forgot to mention it.
    [Fact]
    public async Task SaveAsync_ReplacesTheRowWholesaleRatherThanPatchingIt()
    {
        var run = await AddRunAsync();

        await _service.SaveAsync(run.Id, "emp-1", Dto(
            otNormal: 5m,
            items: [new ManualLineItemDto { Category = "wages_bonus_annual", Amount = 500m }],
            notes: "first"));

        var second = await _service.SaveAsync(run.Id, "emp-1", Dto(otNormal: 2m));

        Assert.True(second.Ok);
        Assert.Equal(2m, second.Adjustment!.OtNormalHours);
        Assert.Empty(second.Adjustment.ManualLineItems);
        Assert.Null(second.Adjustment.Notes);
    }

    [Fact]
    public async Task SaveAsync_KeepsOneRowPerEmployeeAcrossRepeatedSaves()
    {
        var run = await AddRunAsync();

        await _service.SaveAsync(run.Id, "emp-1", Dto(otNormal: 1m));
        await _service.SaveAsync(run.Id, "emp-1", Dto(otNormal: 2m));

        Assert.Single(await _service.GetForRunAsync(run.Id));
    }

    // The calculator SKIPS an unrecognised category rather than failing, which
    // is right for a month's payroll but wrong for a form submission: without
    // this guard the admin's row would simply vanish at generation time with
    // nothing to explain it.
    [Fact]
    public async Task SaveAsync_RefusesAnUnknownCategory()
    {
        var run = await AddRunAsync();

        var result = await _service.SaveAsync(run.Id, "emp-1", Dto(
            items: [new ManualLineItemDto { Category = "not_a_category", Amount = 10m }]));

        Assert.True(result.Found);
        Assert.False(result.Ok);
        Assert.Contains("not_a_category", result.Error);
        Assert.Empty(await _service.GetForRunAsync(run.Id));
    }

    // Kind comes off the catalogue, never off the request, so what is stored
    // cannot contradict what the calculator will actually do with the row.
    [Fact]
    public async Task SaveAsync_DerivesTheLineKindFromTheCategory()
    {
        var run = await AddRunAsync();

        var result = await _service.SaveAsync(run.Id, "emp-1", Dto(items:
        [
            new ManualLineItemDto { Category = "deduct_advance", Amount = 200m },
            new ManualLineItemDto { Category = "wages_bonus_annual", Amount = 500m },
        ]));

        var items = result.Adjustment!.ManualLineItems;
        Assert.Equal(PayslipLineKind.DEDUCTION, items[0].Kind);
        Assert.Equal(PayslipLineKind.ALLOWANCE, items[1].Kind);
    }

    [Fact]
    public async Task SaveAsync_RoundTripsOverrides()
    {
        var run = await AddRunAsync();

        var result = await _service.SaveAsync(run.Id, "emp-1", Dto(overrides: new()
        {
            ["0"] = new FixedAllowanceOverrideDto { Amount = 250m },
            ["2"] = new FixedAllowanceOverrideDto { Skip = true },
        }));

        var overrides = result.Adjustment!.FixedAllowanceOverrides;
        Assert.Equal(250m, overrides["0"].Amount);
        Assert.True(overrides["2"].Skip);
        Assert.Null(overrides["2"].Amount);
    }

    // A submitted run's figures have been filed. Accepting an edit here would
    // either do nothing or, worse, look as though it had.
    [Theory]
    [InlineData(PayrollRunStatus.PENDING_APPROVAL)]
    [InlineData(PayrollRunStatus.SUBMITTED)]
    public async Task SaveAsync_RefusesANonDraftRun(PayrollRunStatus status)
    {
        var run = await AddRunAsync(status);

        var result = await _service.SaveAsync(run.Id, "emp-1", Dto(otNormal: 5m));

        Assert.True(result.Found);
        Assert.False(result.Ok);
        Assert.Empty(await _service.GetForRunAsync(run.Id));
        Assert.Null((await _runs.GetByIdAsync(run.Id))!.LastMutatedAt);
    }

    [Fact]
    public async Task SaveAsync_ReportsAMissingRunAsNotFound()
    {
        var result = await _service.SaveAsync("nope", "emp-1", Dto());

        Assert.False(result.Found);
    }

    // ─── Clearing ───────────────────────────────────────────────────────

    [Fact]
    public async Task ClearAsync_RemovesTheRowAndMarksTheRunStale()
    {
        var run = await AddRunAsync();
        await _service.SaveAsync(run.Id, "emp-1", Dto(otNormal: 4m));
        await _runs.UpdateAsync(await ResetMutatedAsync(run.Id));

        var result = await _service.ClearAsync(run.Id, "emp-1");

        Assert.True(result.Ok);
        Assert.Empty(await _service.GetForRunAsync(run.Id));
        Assert.NotNull((await _runs.GetByIdAsync(run.Id))!.LastMutatedAt);
        Assert.True(_audit.Recorded(AuditActions.PayrollRunAdjustmentClear));
    }

    // Clearing what was never there is what the caller asked for, so it is not
    // an error — but nothing changed, so the run must not be marked stale and
    // sent for a pointless regeneration.
    [Fact]
    public async Task ClearAsync_OnAnAbsentRowSucceedsWithoutMarkingTheRunStale()
    {
        var run = await AddRunAsync();

        var result = await _service.ClearAsync(run.Id, "emp-1");

        Assert.True(result.Ok);
        Assert.Null((await _runs.GetByIdAsync(run.Id))!.LastMutatedAt);
        Assert.False(_audit.Recorded(AuditActions.PayrollRunAdjustmentClear));
    }

    [Theory]
    [InlineData(PayrollRunStatus.PENDING_APPROVAL)]
    [InlineData(PayrollRunStatus.SUBMITTED)]
    public async Task ClearAsync_RefusesANonDraftRun(PayrollRunStatus status)
    {
        var run = await AddRunAsync(status);
        _db.PayrollRunAdjustments.Add(new PayrollRunAdjustment
        {
            OrganizationId = "org-1",
            PayrollRunId = run.Id,
            EmployeeProfileId = "emp-1",
            OtNormalHours = 4m,
        });
        await _db.SaveChangesAsync();

        var result = await _service.ClearAsync(run.Id, "emp-1");

        Assert.True(result.Found);
        Assert.False(result.Ok);
        Assert.Single(await _service.GetForRunAsync(run.Id));
    }

    // ─── Tenancy ────────────────────────────────────────────────────────

    // The adjustment tables carry their own OrganizationId and their own query
    // filter. Without them the reference schema's shape — keyed off the
    // employee profile alone — would leave another org's typed-in overtime
    // readable here.
    [Fact]
    public async Task GetForRunAsync_CannotSeeAnotherOrgsAdjustments()
    {
        var run = await AddRunAsync(organizationId: "org-2");
        _db.PayrollRunAdjustments.Add(new PayrollRunAdjustment
        {
            OrganizationId = "org-2",
            PayrollRunId = run.Id,
            EmployeeProfileId = "emp-1",
            OtNormalHours = 9m,
        });
        await _db.SaveChangesAsync();

        Assert.Empty(await _service.GetForRunAsync(run.Id));
    }

    private async Task<PayrollRun> ResetMutatedAsync(string runId)
    {
        var run = await _runs.GetByIdAsync(runId);
        run!.LastMutatedAt = null;
        return run;
    }
}
