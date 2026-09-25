using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Email;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// Manual, admin-triggered payslip email — per employee and for a whole run.
// Against a real EF context for the run/payslip repositories (same pattern as
// PayrollRunServiceTests), with the PDF renderer and mail sender faked: what
// this service owns is the SUBMITTED gate, the recipient lookup, and the
// bulk loop's non-blocking failure handling — not PDF rendering, which has
// its own tests.
public class PayslipEmailServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new() { OrganizationId = "org-1" };
    private readonly FakeAuditService _audit = new();
    private readonly FakeStatutoryFiles _statutory = new();
    private readonly FakeEmailSender _email = new();
    private readonly PayslipEmailService _service;

    public PayslipEmailServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payslip-email-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        var directory = TestDirectory.Over(
            new OrganizationMembershipRepository(_db),
            new UserRepository(_db));

        _service = new PayslipEmailService(
            new PayslipRepository(_db),
            new PayrollRunRepository(_db),
            _statutory,
            new EmployeeRowResolver(directory),
            new StubOrganizations(),
            _currentUser,
            _email,
            _audit);
    }

    public void Dispose() => _db.Dispose();

    private async Task<PayrollRun> SeedRunAsync(PayrollRunStatus status)
    {
        var run = new PayrollRun
        {
            Id = $"run-{Guid.NewGuid()}",
            OrganizationId = "org-1",
            PeriodYear = 2026,
            PeriodMonth = 1,
            Status = status,
        };
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    private async Task<Payslip> SeedPayslipAsync(
        PayrollRun run, string userId, string employeeProfileId, string name)
    {
        var payslip = new Payslip
        {
            Id = $"slip-{Guid.NewGuid()}",
            OrganizationId = "org-1",
            PayrollRunId = run.Id,
            EmployeeProfileId = employeeProfileId,
            UserId = userId,
            SnapshotName = name,
        };
        _db.Payslips.Add(payslip);
        await _db.SaveChangesAsync();
        return payslip;
    }

    private async Task SeedMemberAsync(string userId, string email, string name)
    {
        _db.Users.Add(new User { Id = userId, Email = email, Name = name, CreatedAt = DateTime.UtcNow });
        _db.OrganizationMemberships.Add(new OrganizationMembership
        {
            Id = $"mem-{userId}", OrganizationId = "org-1", UserId = userId, Role = "Employee",
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task EmailPayslipAsync_RefusesANonSubmittedRun()
    {
        var run = await SeedRunAsync(PayrollRunStatus.DRAFT);
        var payslip = await SeedPayslipAsync(run, "usr-1", "emp-1", "Aisyah");
        await SeedMemberAsync("usr-1", "aisyah@example.com", "Aisyah");

        var result = await _service.EmailPayslipAsync(run.Id, payslip.EmployeeProfileId);

        Assert.False(result.Ok);
        Assert.Contains("not been approved", result.Error);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task EmailPayslipAsync_SendsTheRenderedPdfAsAnAttachment()
    {
        var run = await SeedRunAsync(PayrollRunStatus.SUBMITTED);
        var payslip = await SeedPayslipAsync(run, "usr-1", "emp-1", "Aisyah");
        await SeedMemberAsync("usr-1", "aisyah@example.com", "Aisyah");
        _statutory.Pdf = new StatutoryFileResult(true, "payslip-jan-2026.pdf", [1, 2, 3], "application/pdf", null);

        var result = await _service.EmailPayslipAsync(run.Id, payslip.EmployeeProfileId);

        Assert.True(result.Ok);
        var sent = Assert.Single(_email.Sent);
        Assert.Equal("aisyah@example.com", sent.To);
        Assert.Contains("January 2026", sent.Subject);
        var attachment = Assert.Single(sent.Attachments!);
        Assert.Equal("payslip-jan-2026.pdf", attachment.FileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, attachment.Content);

        // One successful send, one audit row.
        Assert.True(_audit.Recorded(AuditActions.PayrollRunPayslipEmail));
    }

    [Fact]
    public async Task EmailPayslipAsync_FailsCleanlyWithNoEmailOnFile()
    {
        var run = await SeedRunAsync(PayrollRunStatus.SUBMITTED);
        var payslip = await SeedPayslipAsync(run, "usr-ghost", "emp-1", "Nobody");
        // Deliberately no SeedMemberAsync — the user doesn't resolve.
        _statutory.Pdf = new StatutoryFileResult(true, "x.pdf", [1], "application/pdf", null);

        var result = await _service.EmailPayslipAsync(run.Id, payslip.EmployeeProfileId);

        Assert.False(result.Ok);
        Assert.Contains("No email address", result.Error);
        Assert.Empty(_email.Sent);
        Assert.Empty(_audit.Written);
    }

    [Fact]
    public async Task EmailPayslipsForRunAsync_OneFailureDoesNotStopOrSkipTheRest()
    {
        var run = await SeedRunAsync(PayrollRunStatus.SUBMITTED);
        await SeedPayslipAsync(run, "usr-1", "emp-1", "Aisyah");
        await SeedMemberAsync("usr-1", "aisyah@example.com", "Aisyah");
        await SeedPayslipAsync(run, "usr-ghost", "emp-2", "Nobody"); // no email on file
        await SeedPayslipAsync(run, "usr-3", "emp-3", "Farid");
        await SeedMemberAsync("usr-3", "farid@example.com", "Farid");
        _statutory.Pdf = new StatutoryFileResult(true, "x.pdf", [1], "application/pdf", null);

        var result = await _service.EmailPayslipsForRunAsync(run.Id);

        Assert.Equal(2, result.Sent);
        var failure = Assert.Single(result.Failed);
        Assert.Equal("Nobody", failure.EmployeeName);
        Assert.Equal(2, _email.Sent.Count);
    }

    [Fact]
    public async Task EmailPayslipsForRunAsync_RefusesANonSubmittedRun()
    {
        var run = await SeedRunAsync(PayrollRunStatus.PENDING_APPROVAL);

        var result = await _service.EmailPayslipsForRunAsync(run.Id);

        Assert.Equal(0, result.Sent);
        Assert.Empty(result.Failed);
        Assert.NotNull(result.Error);
    }

    // ─── Fakes ────────────────────────────────────────────────────────────

    private sealed class StubOrganizations : IOrganizationRepository
    {
        public Task<Organization?> GetByIdAsync(string id) =>
            Task.FromResult<Organization?>(new Organization { Id = id, Name = "Acme Sdn Bhd" });
        public Task<Organization?> GetFirstAsync() => throw new NotSupportedException();
        public Task<List<Organization>> GetAllAsync() => throw new NotSupportedException();
        public Task<bool> AnyAsync() => throw new NotSupportedException();
        public Task AddAsync(Organization organization) => throw new NotSupportedException();
        public Task UpdateAsync(Organization organization) => throw new NotSupportedException();
    }

    // Returns whatever `Pdf` is set to for every call — this service's own
    // tests don't need per-employee PDF content, just that the bytes/filename
    // it gets back are the ones that end up on the email.
    private sealed class FakeStatutoryFiles : IStatutoryFileService
    {
        public StatutoryFileResult Pdf { get; set; } = new(false, null, null, null, "not set up");

        public Task<StatutoryFileResult> RenderPayslipPdfAsync(string runId, string employeeProfileId) =>
            Task.FromResult(Pdf);

        public Task<StatutoryFileResult> RenderEpfCsvAsync(string runId) => throw new NotSupportedException();
        public Task<StatutoryFileResult> RenderPerkesoTxtAsync(string runId) => throw new NotSupportedException();
        public Task<StatutoryFileResult> RenderPerkesoSkbbkTxtAsync(string runId) => throw new NotSupportedException();
        public Task<StatutoryFileResult> RenderPcbTxtAsync(string runId) => throw new NotSupportedException();
        public Task<PayrollRunReadiness.Result?> GetReadinessAsync(string runId) =>
            throw new NotSupportedException();
        public Task<StatutoryFileResult> RenderAllPayslipsZipAsync(string runId) =>
            throw new NotSupportedException();
        public Task<StatutoryFileResult> RenderSummaryPdfAsync(string runId) => throw new NotSupportedException();
        public Task<StatutoryFileResult> RenderPaymentSchedulePdfAsync(string runId) =>
            throw new NotSupportedException();
        public Task<StatutoryFileResult> RenderPcbDetailsPdfAsync(string runId) =>
            throw new NotSupportedException();
        public Task<StatutoryFileResult> RenderBankFileAsync(
            string runId, DateTime? paymentDate, string? recipientReference = null, HlbChannel? channel = null) =>
            throw new NotSupportedException();
    
    // The bundle is an integration surface; payslip email does not build one.
    public Task<AltomateHR.Api.Modules.Payroll.PayrollBundleResult> RenderRunBundleAsync(
        string runId, DateTime? paymentDate) => throw new NotSupportedException();
}

    private sealed record SentEmail(
        string To, string Subject, string Html, IReadOnlyList<EmailAttachment>? Attachments);

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<SentEmail> Sent { get; } = [];
        public bool Succeeds { get; set; } = true;

        public Task<bool> SendAsync(
            string toEmail, string subject, string htmlBody,
            IReadOnlyList<EmailAttachment>? attachments = null,
            CancellationToken cancellationToken = default)
        {
            Sent.Add(new SentEmail(toEmail, subject, htmlBody, attachments));
            return Task.FromResult(Succeeds);
        }
    }
}
