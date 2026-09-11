using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// An employee's own payslips.
//
// Everything here is about the BOUNDARY: the caller sees their own payslips
// for submitted runs and nothing else. The figures themselves are covered
// where they are computed.
public class EmployeePayrollServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new() { UserId = "usr-1" };
    private readonly EmployeePayrollService _service;

    public EmployeePayrollServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"employee-payroll-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        var profiles = new EmployeeProfileRepository(_db);

        _service = new EmployeePayrollService(
            new PayslipRepository(_db),
            profiles,
            new StatutoryFileService(
                new PayrollRunRepository(_db),
                new PayslipRepository(_db),
                new PayrollCompanyInfoRepository(_db),
                TestDirectory.Over(
                    new OrganizationMembershipRepository(_db),
                    new AltomateHR.Api.Modules.Auth.UserRepository(_db),
                    profiles),
                new StubPayrollOrganizations(),
                _currentUser,
                new PayrollSettingsService(new PayrollSettingsRepository(_db), new FakeAuditService())),
            _currentUser);

        Seed();
    }

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        _db.Users.Add(new AltomateHR.Api.Modules.Auth.Entities.User
        {
            Id = "usr-1", Email = "aisyah@x.com", Name = "Aisyah Binti Rahman",
        });
        _db.Users.Add(new AltomateHR.Api.Modules.Auth.Entities.User
        {
            Id = "usr-2", Email = "bala@x.com", Name = "Bala",
        });
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "emp-1", OrganizationId = "org-1", UserId = "usr-1",
        });
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = "emp-2", OrganizationId = "org-1", UserId = "usr-2",
        });
        _db.SaveChanges();
    }

    private string SeedPayslip(
        int year, int month,
        PayrollRunStatus status = PayrollRunStatus.SUBMITTED,
        string employeeProfileId = "emp-1",
        decimal net = 4300m)
    {
        var runId = $"run-{year}-{month}";

        if (!_db.PayrollRuns.Any(r => r.Id == runId))
        {
            _db.PayrollRuns.Add(new PayrollRun
            {
                Id = runId,
                OrganizationId = "org-1",
                PeriodYear = year,
                PeriodMonth = month,
                Status = status,
                SubmittedAt = status == PayrollRunStatus.SUBMITTED
                    ? new DateTime(year, month, 28)
                    : null,
            });
        }

        var payslipId = $"slip-{employeeProfileId}-{year}-{month}";
        _db.Payslips.Add(new Payslip
        {
            Id = payslipId,
            OrganizationId = "org-1",
            PayrollRunId = runId,
            EmployeeProfileId = employeeProfileId,
            UserId = employeeProfileId == "emp-1" ? "usr-1" : "usr-2",
            SnapshotName = "Aisyah Binti Rahman",
            SnapshotEmployeeNumber = "E-001",
            GrossPay = 5000m,
            NetPay = net,
            EpfEmployee = 550m,
            Pcb = 110m,
        });
        _db.SaveChanges();

        return payslipId;
    }

    // ─── The list ───────────────────────────────────────────────────────

    [Fact]
    public async Task MyPayslips_AreNewestFirst()
    {
        SeedPayslip(2026, 1);
        SeedPayslip(2026, 3);
        SeedPayslip(2026, 2);

        var payslips = await _service.GetMyPayslipsAsync();

        Assert.Equal([3, 2, 1], payslips.Select(p => p.PeriodMonth));
        Assert.Equal("March 2026", payslips[0].PeriodLabel);
        Assert.Equal(new DateTime(2026, 3, 28), payslips[0].SubmittedAt);
    }

    // A draft is still being edited. An employee seeing a figure that may
    // still move is worse than seeing nothing yet.
    [Theory]
    [InlineData(PayrollRunStatus.DRAFT)]
    [InlineData(PayrollRunStatus.PENDING_APPROVAL)]
    public async Task AnUnsubmittedRun_IsInvisible(PayrollRunStatus status)
    {
        SeedPayslip(2026, 1, status);

        Assert.Empty(await _service.GetMyPayslipsAsync());
    }

    [Fact]
    public async Task OnlyMyOwnPayslipsAreListed()
    {
        SeedPayslip(2026, 1, employeeProfileId: "emp-1");
        SeedPayslip(2026, 1, employeeProfileId: "emp-2");

        var payslips = await _service.GetMyPayslipsAsync();

        Assert.Single(payslips);
        Assert.Equal("slip-emp-1-2026-1", payslips[0].Id);
    }

    [Fact]
    public async Task SomeoneWithNoProfile_SeesNothing()
    {
        SeedPayslip(2026, 1);
        _currentUser.UserId = "usr-nobody";

        Assert.Empty(await _service.GetMyPayslipsAsync());
    }

    // ─── The detail ─────────────────────────────────────────────────────

    [Fact]
    public async Task MyOwnPayslip_Opens()
    {
        var id = SeedPayslip(2026, 1);

        var payslip = await _service.GetMyPayslipAsync(id);

        Assert.NotNull(payslip);
        Assert.Equal(4300m, payslip.NetPay);
    }

    // Someone else's payslip is a 404, not a 403 — confirming it exists is
    // already more than a prober should learn.
    [Fact]
    public async Task SomeoneElsesPayslip_IsNotFound()
    {
        var theirs = SeedPayslip(2026, 1, employeeProfileId: "emp-2");

        Assert.Null(await _service.GetMyPayslipAsync(theirs));
    }

    [Fact]
    public async Task AnUnsubmittedPayslipOfMine_IsNotFound()
    {
        var id = SeedPayslip(2026, 1, PayrollRunStatus.DRAFT);

        Assert.Null(await _service.GetMyPayslipAsync(id));
    }

    [Fact]
    public async Task AnInventedId_IsNotFound()
    {
        Assert.Null(await _service.GetMyPayslipAsync("slip-does-not-exist"));
    }

    // ─── The PDF ────────────────────────────────────────────────────────

    // The PDF must hold exactly the same boundary as the JSON read, or the
    // gate is decorative.
    [Fact]
    public async Task ThePdfHoldsTheSameBoundary()
    {
        var mine = SeedPayslip(2026, 1, employeeProfileId: "emp-1");
        var theirs = SeedPayslip(2026, 1, employeeProfileId: "emp-2");
        var draft = SeedPayslip(2026, 2, PayrollRunStatus.DRAFT);

        Assert.True((await _service.RenderMyPayslipPdfAsync(mine)).Ok);
        Assert.False((await _service.RenderMyPayslipPdfAsync(theirs)).Ok);
        Assert.False((await _service.RenderMyPayslipPdfAsync(draft)).Ok);
        Assert.False((await _service.RenderMyPayslipPdfAsync("nope")).Ok);
    }

    // ─── Tenant isolation ───────────────────────────────────────────────

    [Fact]
    public async Task AnotherOrgSeesNothingOfOurs()
    {
        SeedPayslip(2026, 1);

        _currentUser.OrganizationId = "org-2";

        Assert.Empty(await _service.GetMyPayslipsAsync());
        Assert.Null(await _service.GetMyPayslipAsync("slip-emp-1-2026-1"));
    }
}
