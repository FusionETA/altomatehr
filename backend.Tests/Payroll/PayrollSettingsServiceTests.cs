using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// The settings service against a real EF context, so the tenant filter and the
// upsert both run for real.
public class PayrollSettingsServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly PayrollSettingsService _service;

    public PayrollSettingsServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-settings-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _service = new PayrollSettingsService(new PayrollSettingsRepository(_db), _audit);
    }

    public void Dispose() => _db.Dispose();

    private static SavePayrollSettingsDto Save(
        WorkingDaysRule rule = WorkingDaysRule.CALENDAR,
        decimal employeeRate = 11m,
        decimal employerRate = 13m,
        bool hrdfEnabled = false,
        decimal? hrdfRate = null) => new()
        {
            WorkingDaysRule = rule,
            DefaultEpfEmployeeRate = employeeRate,
            DefaultEpfEmployerRate = employerRate,
            HrdfEnabled = hrdfEnabled,
            HrdfRate = hrdfRate,
        };

    // ─── Reading before anything is configured ──────────────────────────

    // A brand-new org must still be able to run payroll, so the GET answers
    // with the statutory defaults rather than 404.
    [Fact]
    public async Task AnUnconfiguredOrg_GetsTheStatutoryDefaults()
    {
        var settings = await _service.GetAsync();

        Assert.False(settings.IsConfigured);
        Assert.Equal(WorkingDaysRule.TWENTY_SIX, settings.WorkingDaysRule);
        Assert.Equal(11.00m, settings.DefaultEpfEmployeeRate);
        Assert.Equal(13.00m, settings.DefaultEpfEmployerRate);
        Assert.True(settings.AutoApplySocsoEisRelief);
        Assert.Null(settings.UpdatedAt);
    }

    // A GET must not write. If it did, every read would create a row and the
    // "has this org configured payroll?" signal would be lost forever.
    [Fact]
    public async Task ReadingDoesNotCreateARow()
    {
        await _service.GetAsync();
        await _service.GetAsync();

        Assert.Empty(await _db.PayrollSettings.ToListAsync());
    }

    [Fact]
    public async Task GetEffective_FallsBackToATransientDefault()
    {
        var effective = await _service.GetEffectiveAsync();

        Assert.Equal(WorkingDaysRule.TWENTY_SIX, effective.WorkingDaysRule);
        Assert.Empty(await _db.PayrollSettings.ToListAsync());
    }

    // ─── Saving ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFirstSave_CreatesExactlyOneRow()
    {
        var saved = await _service.SaveAsync(Save(WorkingDaysRule.CALENDAR, employeeRate: 9m));

        Assert.True(saved.IsConfigured);
        Assert.Equal(WorkingDaysRule.CALENDAR, saved.WorkingDaysRule);
        Assert.Equal(9m, saved.DefaultEpfEmployeeRate);
        Assert.NotNull(saved.UpdatedAt);

        var rows = await _db.PayrollSettings.ToListAsync();
        Assert.Single(rows);
        Assert.Equal("org-1", rows[0].OrganizationId);   // auto-stamped
    }

    // Saving twice must update in place — a second row would make "the org's
    // settings" ambiguous.
    [Fact]
    public async Task ASecondSave_UpdatesInPlace()
    {
        await _service.SaveAsync(Save(WorkingDaysRule.CALENDAR));
        var updated = await _service.SaveAsync(Save(WorkingDaysRule.TWENTY_SIX, employerRate: 12m));

        Assert.Equal(WorkingDaysRule.TWENTY_SIX, updated.WorkingDaysRule);
        Assert.Equal(12m, updated.DefaultEpfEmployerRate);
        Assert.Single(await _db.PayrollSettings.ToListAsync());
    }

    [Fact]
    public async Task TheFirstSave_PreservesCreatedAt()
    {
        await _service.SaveAsync(Save());
        var created = (await _db.PayrollSettings.SingleAsync()).CreatedAt;

        await _service.SaveAsync(Save(employerRate: 12m));
        var afterUpdate = await _db.PayrollSettings.SingleAsync();

        Assert.Equal(created, afterUpdate.CreatedAt);
        Assert.True(afterUpdate.UpdatedAt >= created);
    }

    // Keeping a rate on a disabled levy invites it silently reactivating when
    // someone flips the toggle back on months later.
    [Fact]
    public async Task DisablingHrdf_ClearsItsRate()
    {
        await _service.SaveAsync(Save(hrdfEnabled: true, hrdfRate: 1m));
        var disabled = await _service.SaveAsync(Save(hrdfEnabled: false, hrdfRate: 1m));

        Assert.False(disabled.HrdfEnabled);
        Assert.Null(disabled.HrdfRate);
    }

    [Fact]
    public async Task EnablingHrdf_KeepsItsRate()
    {
        var saved = await _service.SaveAsync(Save(hrdfEnabled: true, hrdfRate: 0.5m));

        Assert.True(saved.HrdfEnabled);
        Assert.Equal(0.5m, saved.HrdfRate);
    }

    [Fact]
    public async Task GetEffective_ReturnsWhatWasSaved()
    {
        await _service.SaveAsync(Save(WorkingDaysRule.CALENDAR));

        Assert.Equal(WorkingDaysRule.CALENDAR, (await _service.GetEffectiveAsync()).WorkingDaysRule);
    }

    // ─── Audit ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EverySave_IsAudited()
    {
        await _service.SaveAsync(Save());
        await _service.SaveAsync(Save(employerRate: 12m));

        Assert.Equal(2, _audit.Written.Count(e => e.Action == "payroll.settings.update"));
        Assert.Contains("Configured", _audit.Written[0].Summary);
        Assert.Contains("Updated", _audit.Written[1].Summary);
    }

    // ─── Tenant isolation ───────────────────────────────────────────────

    // One org's settings must be invisible to another. The global query filter
    // does this, but payroll rules decide what everyone gets paid, so the
    // guarantee is worth an explicit test.
    [Fact]
    public async Task AnotherOrgSeesItsOwnSettings_NotOurs()
    {
        await _service.SaveAsync(Save(WorkingDaysRule.CALENDAR, employeeRate: 9m));

        _currentUser.OrganizationId = "org-2";

        var otherOrg = await _service.GetAsync();
        Assert.False(otherOrg.IsConfigured);
        Assert.Equal(WorkingDaysRule.TWENTY_SIX, otherOrg.WorkingDaysRule);

        // And saving for org-2 leaves org-1's row alone.
        await _service.SaveAsync(Save(WorkingDaysRule.TWENTY_SIX, employeeRate: 15m));

        _currentUser.OrganizationId = "org-1";
        var ours = await _service.GetAsync();
        Assert.Equal(9m, ours.DefaultEpfEmployeeRate);

        Assert.Equal(2, await _db.PayrollSettings.IgnoreQueryFilters().CountAsync());
    }
}
