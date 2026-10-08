using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiKeys.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// The AB Pay timesheet is for ABPay-connected companies only (an active API
// key named "ABPay…"). Refused at the service — the one chokepoint the
// download route passes through — so a direct URL cannot get around it.
public class AbPayExportGateTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new() { OrganizationId = "org-1" };
    private readonly StubPayrollOrganizations _organizations = new() { Name = "Ayu Borneo (Management)" };
    private readonly StatutoryFileService _service;

    public AbPayExportGateTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"abpay-export-gate-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new AltomateHR.Api.Modules.Auth.UserRepository(_db),
            new EmployeeProfileRepository(_db));

        _service = new StatutoryFileService(
            new PayrollRunRepository(_db),
            new PayslipRepository(_db),
            new PayrollCompanyInfoRepository(_db),
            directory,
            _organizations,
            _currentUser,
            new PayrollSettingsService(new PayrollSettingsRepository(_db), new FakeAuditService()),
            new ApiKeyService(new ApiKeyRepository(_db)));
    }

    public void Dispose() => _db.Dispose();

    private async Task AddKeyAsync(string name, bool active = true)
    {
        _db.ApiKeys.Add(new ApiKey
        {
            Name = name,
            TokenHash = Guid.NewGuid().ToString("N"),
            TokenPrefix = "wp_live_x",
            Active = active,
        });
        await _db.SaveChangesAsync();
    }

    // An approved run with one payslip — everything the export needs.
    private async Task<string> SeedApprovedRunAsync()
    {
        var run = new PayrollRun { PeriodYear = 2026, PeriodMonth = 8, Status = PayrollRunStatus.SUBMITTED };
        _db.PayrollRuns.Add(run);
        _db.Payslips.Add(new Payslip
        {
            PayrollRunId = run.Id,
            EmployeeProfileId = "ep-1",
            UserId = "usr-1",
            SnapshotName = "Rubiah Binti Katim",
            SnapshotEmployeeNumber = "00027",
            BasicPay = 2000m,
            ProratedPay = 2000m,
            GrossPay = 2000m,
            NetPay = 2000m,
        });
        await _db.SaveChangesAsync();
        return run.Id;
    }

    private const string NotConnected =
        "AB Pay export is only available for companies connected to ABPay (an active API key named \"ABPay…\").";

    [Fact]
    public async Task WithoutAnAbPayKey_IsRefused()
    {
        var runId = await SeedApprovedRunAsync();

        var result = await _service.RenderAbPayTimesheetXlsxAsync(runId);

        Assert.False(result.Ok);
        Assert.Equal(NotConnected, result.Error);   // a message → the controller answers 409
    }

    [Theory]
    [InlineData("Xero sync", true)]
    [InlineData("ABPay importer", false)]
    public async Task AnotherKeyOrARevokedOne_IsRefused(string name, bool active)
    {
        await AddKeyAsync(name, active);
        var runId = await SeedApprovedRunAsync();

        var result = await _service.RenderAbPayTimesheetXlsxAsync(runId);

        Assert.False(result.Ok);
        Assert.Equal(NotConnected, result.Error);
    }

    // Connected: the file comes back, with the full company name in Company.
    [Fact]
    public async Task WithAnActiveAbPayKey_ProducesTheFile_WithTheCompanyName()
    {
        await AddKeyAsync("ABPay importer");
        var runId = await SeedApprovedRunAsync();

        var result = await _service.RenderAbPayTimesheetXlsxAsync(runId);

        Assert.True(result.Ok, result.Error);
        Assert.Equal("ABPay Ayu Borneo (Management) 2026-08.xlsx", result.FileName);
        using var workbook = new XLWorkbook(new MemoryStream(result.Content!));
        Assert.Equal("Ayu Borneo (Management)", workbook.Worksheet(1).Cell(2, 2).GetString());
    }

    [Fact]
    public async Task WithAnActiveAbPayKey_AMissingRunIsStillNotFound()
    {
        await AddKeyAsync("ABPay importer");

        var result = await _service.RenderAbPayTimesheetXlsxAsync("no-such-run");

        Assert.False(result.Ok);
        Assert.Null(result.Error);   // null error → the controller answers 404
    }

    // The run zip is the filing/payment bundle; the AB Pay sheet is not part of it.
    [Fact]
    public async Task TheRunBundle_NeverIncludesTheAbPayTimesheet()
    {
        await AddKeyAsync("ABPay importer");
        var runId = await SeedApprovedRunAsync();

        var bundle = await _service.RenderRunBundleAsync(runId, paymentDate: null);

        Assert.True(bundle.Ok);
        var names = bundle.Included.Concat(bundle.Skipped.Keys).ToList();
        Assert.DoesNotContain("ab-pay-timesheet", names);
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bundle.Content!));
        Assert.DoesNotContain(zip.Entries, e => e.Name.StartsWith("ABPay", StringComparison.OrdinalIgnoreCase));
    }
}
