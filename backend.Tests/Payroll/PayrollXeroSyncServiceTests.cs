using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Accounts;
using AltomateHR.Api.Modules.Accounts.Entities;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Xero;
using AltomateHR.Api.Modules.Xero.Dtos;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;

namespace AltomateHR.Api.Tests.Payroll;

// Posting a run to Xero.
//
// The journal arithmetic is covered by PayrollJournalTests; what is pinned
// here is the orchestration around it — that a run posts at most once, that a
// refusal is recorded rather than thrown, and that an approval never fails
// because Xero was unreachable.
public class PayrollXeroSyncServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly RecordingXeroService _xero = new();
    private readonly PayrollXeroSyncService _service;

    public PayrollXeroSyncServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-xero-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        _service = new PayrollXeroSyncService(
            new PayrollRunRepository(_db),
            new PayslipRepository(_db),
            new PayrollSettingsService(new PayrollSettingsRepository(_db), _audit),
            new ChartOfAccountRepository(_db),
            new FakeClaimsService(),
            _xero,
            _audit,
            NullLogger<PayrollXeroSyncService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    // ─── Fixtures ───────────────────────────────────────────────────────

    // Every slot mapped, and a chart of accounts where each mapped Xero id
    // resolves to a code.
    private async Task SeedMappingAsync(bool syncOnApproval = false)
    {
        foreach (var slot in PayrollXeroAccounts.All)
        {
            _db.ChartOfAccounts.Add(new ChartOfAccount
            {
                Id = $"local-{slot}",
                OrganizationId = "org-1",
                Code = $"CODE-{slot}",
                Name = slot,
                XeroAccountId = $"xero-{slot}",
            });
        }

        var mapping = new PayrollXeroMapping
        {
            Accounts = PayrollXeroAccounts.All.ToDictionary(
                slot => slot, slot => (string?)$"xero-{slot}", StringComparer.Ordinal),
        };

        _db.PayrollSettings.Add(new PayrollSettings
        {
            OrganizationId = "org-1",
            SyncPayrollToXeroOnSubmit = syncOnApproval,
            XeroMappingJson = JsonSerializer.Serialize(mapping, PayrollSnapshotJson.Options),
        });

        await _db.SaveChangesAsync();
    }

    private async Task<PayrollRun> SeedRunAsync(
        PayrollRunStatus status = PayrollRunStatus.SUBMITTED,
        string? journalId = null)
    {
        var run = new PayrollRun
        {
            Id = "run-1",
            OrganizationId = "org-1",
            PeriodYear = 2026,
            PeriodMonth = 3,
            Status = status,
            XeroManualJournalId = journalId,
        };
        _db.PayrollRuns.Add(run);

        // Net derived from its own components, so the journal balances.
        _db.Payslips.Add(new Payslip
        {
            Id = "slip-1",
            OrganizationId = "org-1",
            PayrollRunId = run.Id,
            EmployeeProfileId = "emp-1",
            UserId = "usr-1",
            SnapshotName = "Aisyah Binti Rahman",
            SnapshotEmployeeNumber = "E-001",
            ProratedPay = 5000m,
            GrossPay = 5000m,
            EpfEmployee = 550m,
            EpfEmployer = 650m,
            SocsoEmployee = 24.75m,
            SocsoEmployer = 86.65m,
            EisEmployee = 9.90m,
            EisEmployer = 9.90m,
            Pcb = 110m,
            NetPay = 5000m - (550m + 24.75m + 9.90m + 110m),
        });

        await _db.SaveChangesAsync();
        return run;
    }

    private PayrollRun Reload() => _db.PayrollRuns.AsNoTracking().Single();

    // ─── Posting ────────────────────────────────────────────────────────

    [Fact]
    public async Task AnApprovedRun_Posts()
    {
        await SeedMappingAsync();
        await SeedRunAsync();

        var result = await _service.SyncAsync("run-1");

        Assert.True(result.Ok, result.Error);
        Assert.Equal("mj-1", result.ManualJournalId);
        Assert.Single(_xero.Posted);

        var run = Reload();
        Assert.Equal("mj-1", run.XeroManualJournalId);
        Assert.Equal(Modules.Claims.Entities.XeroSyncStatus.SYNCED, run.XeroSyncStatus);
        Assert.NotNull(run.XeroSyncedAt);
        Assert.Null(run.XeroSyncError);
    }

    // The journal belongs to the period, and the narration is how an
    // accountant finds it in Xero's list.
    [Fact]
    public async Task ThePostedJournalIsDatedAndNamedForThePeriod()
    {
        await SeedMappingAsync();
        await SeedRunAsync();

        await _service.SyncAsync("run-1");

        var posted = _xero.Posted.Single();
        Assert.Equal(new DateTime(2026, 3, 31), posted.Date);
        Assert.Equal("2026 - SALARY FOR MARCH 2026", posted.Narration);
    }

    // ─── Posting at most once ───────────────────────────────────────────

    // Pressing the button twice should read as "nothing to do", and above all
    // must not post the month again.
    [Fact]
    public async Task ASecondSync_DoesNotPostAgain()
    {
        await SeedMappingAsync();
        await SeedRunAsync();

        await _service.SyncAsync("run-1");
        var second = await _service.SyncAsync("run-1");

        Assert.True(second.Ok);
        Assert.True(second.AlreadyPosted);
        Assert.Single(_xero.Posted);
    }

    // Belt and braces behind the stored id: an unchanged retry sends the same
    // Idempotency-Key, so even a post we never learned had succeeded returns
    // the original journal instead of creating a second.
    [Fact]
    public async Task AnUnchangedRetry_ReusesTheIdempotencyKey()
    {
        await SeedMappingAsync();
        await SeedRunAsync();
        await _service.SyncAsync("run-1");

        // Simulate never having learned the first attempt landed.
        var run = _db.PayrollRuns.Single();
        run.XeroManualJournalId = null;
        await _db.SaveChangesAsync();

        await _service.SyncAsync("run-1");

        Assert.Equal(2, _xero.Posted.Count);
        Assert.Equal(_xero.Posted[0].IdempotencyKey, _xero.Posted[1].IdempotencyKey);
        Assert.StartsWith("payroll-run-run-1-", _xero.Posted[0].IdempotencyKey);
    }

    // ─── Refusals ───────────────────────────────────────────────────────

    // A draft is still being edited and has no approval behind it.
    [Theory]
    [InlineData(PayrollRunStatus.DRAFT)]
    [InlineData(PayrollRunStatus.PENDING_APPROVAL)]
    public async Task AnUnapprovedRun_IsRefused(PayrollRunStatus status)
    {
        await SeedMappingAsync();
        await SeedRunAsync(status);

        var result = await _service.SyncAsync("run-1");

        Assert.False(result.Ok);
        Assert.Contains("Approve this run first", result.Error);
        Assert.Empty(_xero.Posted);
    }

    [Fact]
    public async Task AnUnconnectedOrg_IsRefused()
    {
        await SeedMappingAsync();
        await SeedRunAsync();
        _xero.Connected = false;

        var result = await _service.SyncAsync("run-1");

        Assert.False(result.Ok);
        Assert.Contains("isn't connected", result.Error);
    }

    // An unconfigured mapping is the admin's to fix, so it comes back as a
    // message naming the accounts — and is recorded on the run so the page
    // can show why the last attempt did not land.
    [Fact]
    public async Task AnUnmappedChartOfAccounts_IsRecordedNotThrown()
    {
        await SeedRunAsync();   // no mapping seeded

        var result = await _service.SyncAsync("run-1");

        Assert.False(result.Ok);
        Assert.Contains("Salary", result.Error);
        Assert.Empty(_xero.Posted);

        var run = Reload();
        Assert.Equal(Modules.Claims.Entities.XeroSyncStatus.ERROR, run.XeroSyncStatus);
        Assert.NotNull(run.XeroSyncError);
    }

    // Xero rejecting the post is not a server fault either.
    [Fact]
    public async Task AXeroRejection_IsRecordedOnTheRun()
    {
        await SeedMappingAsync();
        await SeedRunAsync();
        _xero.FailWith = "Xero rejected the payroll journal (401).";

        var result = await _service.SyncAsync("run-1");

        Assert.False(result.Ok);
        Assert.Contains("401", result.Error);

        var run = Reload();
        Assert.Equal(Modules.Claims.Entities.XeroSyncStatus.ERROR, run.XeroSyncStatus);
        Assert.Contains("401", run.XeroSyncError);
    }

    // A retry that works must clear the old message — a stale error beside a
    // posted journal reads as a problem that is not there.
    [Fact]
    public async Task ASuccessfulRetry_ClearsThePreviousError()
    {
        await SeedMappingAsync();
        await SeedRunAsync();

        _xero.FailWith = "Xero was unreachable.";
        await _service.SyncAsync("run-1");
        Assert.NotNull(Reload().XeroSyncError);

        _xero.FailWith = null;
        await _service.SyncAsync("run-1");

        var run = Reload();
        Assert.Null(run.XeroSyncError);
        Assert.Equal(Modules.Claims.Entities.XeroSyncStatus.SYNCED, run.XeroSyncStatus);
    }

    [Fact]
    public async Task AMissingRun_IsNotFound()
    {
        Assert.False((await _service.SyncAsync("nope")).Found);
    }

    // ─── Approval never fails because of Xero ───────────────────────────

    [Fact]
    public async Task OnApproval_PostsOnlyWhenTheOrgOptedIn()
    {
        await SeedMappingAsync(syncOnApproval: false);
        await SeedRunAsync();

        await _service.SyncOnApprovalAsync("run-1");
        Assert.Empty(_xero.Posted);
    }

    [Fact]
    public async Task OnApproval_PostsWhenOptedIn()
    {
        await SeedMappingAsync(syncOnApproval: true);
        await SeedRunAsync();

        await _service.SyncOnApprovalAsync("run-1");

        Assert.Single(_xero.Posted);
    }

    // The whole point of the hook being best-effort: an approval already given
    // must not be undone because an accounting integration threw.
    [Fact]
    public async Task OnApproval_SwallowsAnyFailure()
    {
        await SeedMappingAsync(syncOnApproval: true);
        await SeedRunAsync();
        _xero.ThrowUnexpected = true;

        var ex = await Record.ExceptionAsync(() => _service.SyncOnApprovalAsync("run-1"));

        Assert.Null(ex);
    }

    // ─── Preview ────────────────────────────────────────────────────────

    // The preview exists so an admin sees the journal before the ledger does.
    // It must write nothing and post nothing.
    [Fact]
    public async Task Preview_ShowsTheLinesWithoutPosting()
    {
        await SeedMappingAsync();
        await SeedRunAsync();

        var preview = await _service.PreviewAsync("run-1");

        Assert.NotNull(preview);
        Assert.True(preview.Ok, preview.Error);
        Assert.NotEmpty(preview.Lines);
        Assert.Equal(0m, preview.Balance);
        Assert.Equal(preview.TotalDebits, preview.TotalCredits);

        Assert.Empty(_xero.Posted);
        Assert.Null(Reload().XeroManualJournalId);
    }

    // A draft can be previewed — that is when an admin most wants to look.
    [Fact]
    public async Task Preview_WorksOnADraft()
    {
        await SeedMappingAsync();
        await SeedRunAsync(PayrollRunStatus.DRAFT);

        var preview = await _service.PreviewAsync("run-1");

        Assert.NotNull(preview);
        Assert.True(preview.Ok, preview.Error);
    }

    [Fact]
    public async Task Preview_ReportsTheReasonItCannotPost()
    {
        await SeedRunAsync();   // no mapping

        var preview = await _service.PreviewAsync("run-1");

        Assert.NotNull(preview);
        Assert.False(preview.Ok);
        Assert.Contains("Salary", preview.Error);
    }

    // ─── Doubles ────────────────────────────────────────────────────────

    private sealed record PostedJournal(
        string Narration, DateTime Date, string IdempotencyKey, int LineCount);

    private sealed class RecordingXeroService : IXeroService
    {
        public List<PostedJournal> Posted { get; } = [];
        public bool Connected { get; set; } = true;
        public string? FailWith { get; set; }
        public bool ThrowUnexpected { get; set; }
        private int _next = 1;

        public Task<XeroManualJournalResponse> CreateManualJournalAsync(
            XeroManualJournalRequest journal)
        {
            if (ThrowUnexpected) throw new InvalidOperationException("boom");

            Posted.Add(new PostedJournal(
                journal.Narration, journal.Date, journal.IdempotencyKey, journal.Lines.Count));

            if (FailWith is not null) throw new XeroConnectionException(FailWith);

            return Task.FromResult(new XeroManualJournalResponse($"mj-{_next++}", journal.Narration));
        }

        public Task<IReadOnlyList<XeroTrackingCategoryResponse>> GetTrackingCategoriesAsync() =>
            Task.FromResult<IReadOnlyList<XeroTrackingCategoryResponse>>([]);

        public Task<bool> IsConnectedAsync() => Task.FromResult(Connected);

        public Task<XeroConnectUrlDto> CreateConnectUrlAsync(string? returnUrl) =>
            throw new NotSupportedException();
        public Task<string> CompleteCallbackAsync(string code, string state) =>
            throw new NotSupportedException();
        public Task<XeroStatusDto> GetStatusAsync() => throw new NotSupportedException();
        public Task DisconnectAsync() => throw new NotSupportedException();
        public Task<XeroSyncAccountsResultDto> SyncAccountsAsync() => throw new NotSupportedException();
        public Task<IReadOnlyList<XeroCurrencyResponse>> GetCurrenciesAsync() =>
            throw new NotSupportedException();
        public Task<XeroSyncProjectsResultDto> SyncProjectsAsync() => throw new NotSupportedException();
        public Task<XeroFileContent?> GetFileContentAsync(string fileId) =>
            throw new NotSupportedException();
        public Task<XeroBillResponse> CreateBillAsync(XeroBillRequest bill) =>
            throw new NotSupportedException();
        public Task<XeroSpendResponse> CreateSpendAsync(XeroSpendRequest spend) =>
            throw new NotSupportedException();
    }

}
