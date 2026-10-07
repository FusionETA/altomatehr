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
        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new AltomateHR.Api.Modules.Auth.UserRepository(_db),
            profiles);

        _service = new EmployeePayrollService(
            new PayslipRepository(_db),
            profiles,
            new PayrollAnnualReportService(
                new PayrollRunRepository(_db),
                new PayslipRepository(_db),
                new PayrollCompanyInfoRepository(_db),
                directory,
                new StubPayrollOrganizations(),
                _currentUser),
            new StatutoryFileService(
                new PayrollRunRepository(_db),
                new PayslipRepository(_db),
                new PayrollCompanyInfoRepository(_db),
                directory,
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

    // ─── Form EA ────────────────────────────────────────────────────────

    private void SeedFullYear(int year, string employeeProfileId = "emp-1")
    {
        for (var month = 1; month <= 12; month++) SeedPayslip(year, month, employeeProfileId: employeeProfileId);
    }

    [Fact]
    public async Task EaForms_ListTheYearsIWasPaid_NewestFirst_WithReadiness()
    {
        SeedFullYear(2025);
        SeedPayslip(2026, 1);
        SeedPayslip(2026, 2);

        var forms = await _service.GetMyEaFormsAsync();

        Assert.Equal([2026, 2025], forms.Select(f => f.Year));
        Assert.False(forms[0].Available);
        Assert.Equal(2, forms[0].ApprovedMonths);
        Assert.True(forms[1].Available);
    }

    // Readiness is the company's: a month I was not paid in still counts once
    // its run is approved.
    [Fact]
    public async Task EaForm_IsReady_WhenEveryMonthIsApproved_EvenIfIJoinedMidYear()
    {
        SeedFullYear(2025, employeeProfileId: "emp-2");
        SeedPayslip(2025, 11);
        SeedPayslip(2025, 12);

        var form = Assert.Single(await _service.GetMyEaFormsAsync());
        Assert.True(form.Available);

        var pdf = await _service.RenderMyEaFormAsync(2025);
        Assert.True(pdf.Ok);
        Assert.Equal("Form_EA_2025.pdf", pdf.FileName);
    }

    [Fact]
    public async Task EaForm_IsRefused_UntilTheYearIsFullyApproved()
    {
        SeedPayslip(2026, 1);
        SeedPayslip(2026, 2, PayrollRunStatus.DRAFT);

        var pdf = await _service.RenderMyEaFormAsync(2026);

        Assert.False(pdf.Ok);
        Assert.NotNull(pdf.Error);
    }

    // A year I was not paid in is a 404, even when it is ready for others.
    [Fact]
    public async Task EaForm_ForAYearIWasNotPaid_IsNotFound()
    {
        SeedFullYear(2025, employeeProfileId: "emp-2");

        var pdf = await _service.RenderMyEaFormAsync(2025);

        Assert.False(pdf.Ok);
        Assert.Null(pdf.Error);
        Assert.Empty(await _service.GetMyEaFormsAsync());
    }

    [Fact]
    public async Task EaForm_HoldsOnlyMyPage()
    {
        SeedFullYear(2025);
        SeedFullYear(2025, employeeProfileId: "emp-2");

        var mine = await _service.RenderMyEaFormAsync(2025);

        Assert.True(mine.Ok);
        Assert.Equal(1, PageCount(mine.Content!));
    }

    // ─── Form EA for leavers ────────────────────────────────────────────

    private void Leave(DateTime on, string profileId = "emp-1")
    {
        _db.EmployeeProfiles.Single(p => p.Id == profileId).LeaveDate = on;
        _db.SaveChanges();
    }

    // A leaver's year ends when they left, so they need not wait for December.
    [Fact]
    public async Task ALeaver_GetsTheirEa_OnceTheirLeavingMonthIsApproved()
    {
        SeedPayslip(2026, 1);
        SeedPayslip(2026, 2);
        SeedPayslip(2026, 3);
        Leave(new DateTime(2026, 3, 15));

        var form = Assert.Single(await _service.GetMyEaFormsAsync());
        Assert.True(form.Available);
        Assert.Equal(3, form.RequiredMonths);
        Assert.Equal(3, form.ApprovedMonths);

        Assert.True((await _service.RenderMyEaFormAsync(2026)).Ok);
    }

    [Fact]
    public async Task ALeaver_StillWaits_ForTheirLeavingMonth()
    {
        SeedPayslip(2026, 1);
        SeedPayslip(2026, 2);
        Leave(new DateTime(2026, 3, 15));

        var form = Assert.Single(await _service.GetMyEaFormsAsync());
        Assert.False(form.Available);
        Assert.Equal(3, form.RequiredMonths);
        Assert.Equal(2, form.ApprovedMonths);

        var pdf = await _service.RenderMyEaFormAsync(2026);
        Assert.False(pdf.Ok);
        Assert.Contains("up to March", pdf.Error);
    }

    // Final pay approved after the leaving month belongs on the form too.
    [Fact]
    public async Task ALeaversLateFinalPay_ExtendsTheirYear()
    {
        SeedPayslip(2026, 1);
        SeedPayslip(2026, 2);
        SeedPayslip(2026, 4);
        Leave(new DateTime(2026, 2, 20));

        var form = Assert.Single(await _service.GetMyEaFormsAsync());
        Assert.Equal(4, form.RequiredMonths);
        Assert.False(form.Available);

        SeedPayslip(2026, 3, employeeProfileId: "emp-2");

        Assert.True(Assert.Single(await _service.GetMyEaFormsAsync()).Available);
    }

    // Pay still waiting on an unapproved run means the year is not final.
    [Fact]
    public async Task ALeaverWithPayStillPending_IsNotReady()
    {
        SeedPayslip(2026, 1);
        SeedPayslip(2026, 2);
        SeedPayslip(2026, 3, PayrollRunStatus.DRAFT);
        Leave(new DateTime(2026, 2, 28));

        Assert.False(Assert.Single(await _service.GetMyEaFormsAsync()).Available);
        Assert.False((await _service.RenderMyEaFormAsync(2026)).Ok);
    }

    // Leaving NEXT year does not shorten this one.
    [Fact]
    public async Task LeavingInALaterYear_StillNeedsTheFullYear()
    {
        SeedPayslip(2025, 1);
        Leave(new DateTime(2026, 3, 1));

        var form = Assert.Single(await _service.GetMyEaFormsAsync());
        Assert.Equal(12, form.RequiredMonths);
        Assert.False(form.Available);
    }

    // QuestPDF writes one "/Type /Page" object per page.
    private static int PageCount(byte[] pdf) =>
        System.Text.RegularExpressions.Regex.Matches(
            System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page\b").Count;

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
