using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Claims;
using AltomateHR.Api.Modules.Claims.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Modules.Policies.Entities;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.Common;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.Payroll;

// Attaching approved claims to a run so they are reimbursed through pay.
//
// Most of what matters here is what must NOT be possible: paying a
// reimbursement twice, moving a historical figure by editing the claim behind
// it, or taking one off a run that has already been filed.
public class PayrollRunClaimServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeClaimsService _claims = new();
    private readonly PayrollRunClaimService _service;
    private readonly PayrollRunRepository _runs;

    public PayrollRunClaimServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"payroll-run-claims-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);
        _runs = new PayrollRunRepository(_db);

        _service = new PayrollRunClaimService(
            new PayrollRunClaimRepository(_db),
            _runs,
            _claims,
            TestDirectory.Over(
                new OrganizationMembershipRepository(_db),
                new UserRepository(_db),
                new EmployeeProfileRepository(_db)),
            _audit);
    }

    public void Dispose() => _db.Dispose();

    // ─── Fixtures ───────────────────────────────────────────────────────

    private EmployeeProfile AddEmployee(
        string userId, string name, bool isArchived = false, string organizationId = "org-1")
    {
        _db.Users.Add(new User { Id = userId, Email = $"{userId}@x.com", Name = name, PasswordHash = "x" });
        _db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = "Employee",
            EmployeeNumber = "E-001",
        });

        var profile = new EmployeeProfile
        {
            OrganizationId = organizationId,
            UserId = userId,
            SalaryType = SalaryType.MONTHLY,
            MonthlySalary = 5000m,
            IsArchived = isArchived,
        };
        _db.EmployeeProfiles.Add(profile);
        _db.SaveChanges();
        return profile;
    }

    private Claim AddClaim(
        string id, string userId, decimal amount = 120m, string title = "Taxi to site")
    {
        var claim = new Claim
        {
            Id = id,
            OrganizationId = "org-1",
            ClaimNumber = $"CLM-{id}",
            Title = title,
            Amount = amount,
            EmployeeId = userId,
            Status = ClaimStatus.APPROVED,
            PaymentType = PaymentType.PERSONAL,
            Settlement = ClaimSettlement.PAYROLL,
            SpentAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
        };
        _claims.Add(claim);
        return claim;
    }

    private async Task<PayrollRun> AddRunAsync(
        PayrollRunStatus status = PayrollRunStatus.DRAFT, int month = 1)
    {
        var run = new PayrollRun
        {
            OrganizationId = "org-1",
            PeriodYear = 2026,
            PeriodMonth = month,
            Status = status,
            GeneratedAt = new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc),
        };
        _db.PayrollRuns.Add(run);
        await _db.SaveChangesAsync();
        return run;
    }

    // ─── Attaching ──────────────────────────────────────────────────────

    [Fact]
    public async Task AttachAsync_LinksTheClaimToTheRun()
    {
        var profile = AddEmployee("usr-1", "Aisyah");
        AddClaim("clm-1", "usr-1", amount: 120m);
        var run = await AddRunAsync();

        var result = await _service.AttachAsync(run.Id, "clm-1");

        Assert.True(result.Ok);
        Assert.Equal(profile.Id, result.Attachment!.EmployeeProfileId);
        Assert.Equal("Aisyah", result.Attachment.EmployeeName);
        Assert.Equal("CLM-clm-1", result.Attachment.ClaimNumber);
        Assert.True(_audit.Recorded(AuditActions.PayrollRunClaimAttach));
    }

    // The reason Label and Amount are columns here rather than a join: a
    // payroll figure that has been generated — still less filed — must not
    // move because someone corrected the claim behind it afterwards.
    [Fact]
    public async Task AttachAsync_SnapshotsLabelAndAmountAgainstLaterEdits()
    {
        AddEmployee("usr-1", "Aisyah");
        var claim = AddClaim("clm-1", "usr-1", amount: 120m, title: "Taxi to site");
        var run = await AddRunAsync();

        await _service.AttachAsync(run.Id, "clm-1");

        claim.Amount = 999m;
        claim.Title = "Edited later";

        var attached = Assert.Single(await _service.GetForRunAsync(run.Id));
        Assert.Equal(120m, attached.Amount);
        Assert.Equal("Taxi to site", attached.Label);
    }

    [Fact]
    public async Task AttachAsync_MarksTheRunStale()
    {
        AddEmployee("usr-1", "Aisyah");
        AddClaim("clm-1", "usr-1");
        var run = await AddRunAsync();

        await _service.AttachAsync(run.Id, "clm-1");

        Assert.NotNull((await _runs.GetByIdAsync(run.Id))!.LastMutatedAt);
    }

    // A claim sits on at most one run, ever. Attaching it twice is paying it
    // twice — the one failure in this file that costs real money.
    [Fact]
    public async Task AttachAsync_RefusesAClaimAlreadyAttachedElsewhere()
    {
        AddEmployee("usr-1", "Aisyah");
        AddClaim("clm-1", "usr-1");
        var january = await AddRunAsync(month: 1);
        var february = await AddRunAsync(month: 2);

        await _service.AttachAsync(january.Id, "clm-1");
        var again = await _service.AttachAsync(february.Id, "clm-1");

        Assert.False(again.Ok);
        Assert.Contains("January 2026", again.Error);
        Assert.Empty(await _service.GetForRunAsync(february.Id));
    }

    [Fact]
    public async Task AttachAsync_RefusesAResubmissionToTheSameRun()
    {
        AddEmployee("usr-1", "Aisyah");
        AddClaim("clm-1", "usr-1");
        var run = await AddRunAsync();

        await _service.AttachAsync(run.Id, "clm-1");
        var again = await _service.AttachAsync(run.Id, "clm-1");

        Assert.False(again.Ok);
        Assert.Single(await _service.GetForRunAsync(run.Id));
    }

    // There is no one on the payroll to pay it to. The claim is otherwise
    // perfectly valid, so this is a blocked attach, not a broken claim.
    [Fact]
    public async Task AttachAsync_RefusesAClaimWhoseSubmitterHasNoEmployeeProfile()
    {
        _db.Users.Add(new User { Id = "usr-9", Email = "x@x.com", Name = "Contractor", PasswordHash = "x" });
        _db.SaveChanges();
        AddClaim("clm-1", "usr-9");
        var run = await AddRunAsync();

        var result = await _service.AttachAsync(run.Id, "clm-1");

        Assert.False(result.Ok);
        Assert.Contains("employee profile", result.Error);
    }

    [Fact]
    public async Task AttachAsync_RefusesAnArchivedEmployee()
    {
        AddEmployee("usr-1", "Aisyah", isArchived: true);
        AddClaim("clm-1", "usr-1");
        var run = await AddRunAsync();

        var result = await _service.AttachAsync(run.Id, "clm-1");

        Assert.False(result.Ok);
        Assert.Contains("archived", result.Error);
    }

    // PAYROLL settlement never syncs to Xero, so this only fires when the
    // route was switched after the money had already gone out. Reimbursing it
    // again through pay would pay it a second time.
    [Fact]
    public async Task AttachAsync_RefusesAClaimAlreadyBilledToXero()
    {
        AddEmployee("usr-1", "Aisyah");
        var claim = AddClaim("clm-1", "usr-1");
        claim.XeroBillId = "xero-bill-1";
        var run = await AddRunAsync();

        var result = await _service.AttachAsync(run.Id, "clm-1");

        Assert.False(result.Ok);
        Assert.Contains("Xero", result.Error);
    }

    [Theory]
    [InlineData(PayrollRunStatus.PENDING_APPROVAL)]
    [InlineData(PayrollRunStatus.SUBMITTED)]
    public async Task AttachAsync_RefusesANonDraftRun(PayrollRunStatus status)
    {
        AddEmployee("usr-1", "Aisyah");
        AddClaim("clm-1", "usr-1");
        var run = await AddRunAsync(status);

        var result = await _service.AttachAsync(run.Id, "clm-1");

        Assert.True(result.Found);
        Assert.False(result.Ok);
        Assert.Empty(await _service.GetForRunAsync(run.Id));
    }

    [Fact]
    public async Task AttachAsync_ReportsAMissingRunAsNotFound()
    {
        var result = await _service.AttachAsync("nope", "clm-1");

        Assert.False(result.Found);
    }

    [Fact]
    public async Task AttachAsync_ReportsAnUnknownClaim()
    {
        var run = await AddRunAsync();

        var result = await _service.AttachAsync(run.Id, "clm-missing");

        Assert.True(result.Found);
        Assert.False(result.Ok);
        Assert.Contains("Claim not found", result.Error);
    }

    // ─── Detaching ──────────────────────────────────────────────────────

    [Fact]
    public async Task DetachAsync_RemovesTheAttachmentAndMarksTheRunStale()
    {
        AddEmployee("usr-1", "Aisyah");
        AddClaim("clm-1", "usr-1");
        var run = await AddRunAsync();
        await _service.AttachAsync(run.Id, "clm-1");

        var result = await _service.DetachAsync("clm-1");

        Assert.True(result.Ok);
        Assert.Empty(await _service.GetForRunAsync(run.Id));
        Assert.NotNull((await _runs.GetByIdAsync(run.Id))!.LastMutatedAt);
        Assert.True(_audit.Recorded(AuditActions.PayrollRunClaimDetach));
    }

    // The reimbursement has been paid. Taking it off would leave the claim
    // looking unpaid and free to attach to next month as well.
    [Fact]
    public async Task DetachAsync_RefusesToTakeAClaimOffASubmittedRun()
    {
        AddEmployee("usr-1", "Aisyah");
        AddClaim("clm-1", "usr-1");
        var run = await AddRunAsync();
        await _service.AttachAsync(run.Id, "clm-1");

        var submitted = await _runs.GetByIdAsync(run.Id);
        submitted!.Status = PayrollRunStatus.SUBMITTED;
        await _runs.UpdateAsync(submitted);

        var result = await _service.DetachAsync("clm-1");

        Assert.True(result.Found);
        Assert.False(result.Ok);
        Assert.Single(await _service.GetForRunAsync(run.Id));
    }

    [Fact]
    public async Task DetachAsync_ReportsAnUnattachedClaimAsNotFound()
    {
        var result = await _service.DetachAsync("clm-1");

        Assert.False(result.Found);
    }

    // ─── The attachable list ────────────────────────────────────────────

    [Fact]
    public async Task GetAttachableAsync_ListsReimbursableClaimsWithTheirEmployee()
    {
        AddEmployee("usr-1", "Aisyah");
        AddClaim("clm-1", "usr-1", amount: 75m);

        var row = Assert.Single(await _service.GetAttachableAsync());

        Assert.Equal("clm-1", row.ClaimId);
        Assert.Equal("Aisyah", row.EmployeeName);
        Assert.Equal(75m, row.Amount);
        Assert.Null(row.AttachedToRunId);
        Assert.Null(row.BlockedReason);
    }

    // Already-attached claims stay in the list, flagged. Silently omitting them
    // leaves an admin hunting for a claim that looks like it vanished.
    [Fact]
    public async Task GetAttachableAsync_FlagsClaimsAlreadyOnARunRatherThanHidingThem()
    {
        AddEmployee("usr-1", "Aisyah");
        AddClaim("clm-1", "usr-1");
        var run = await AddRunAsync();
        await _service.AttachAsync(run.Id, "clm-1");

        var row = Assert.Single(await _service.GetAttachableAsync());

        Assert.Equal(run.Id, row.AttachedToRunId);
        Assert.Equal("January 2026", row.AttachedToRunPeriod);
    }

    [Fact]
    public async Task GetAttachableAsync_ExplainsWhyAnIneligibleClaimCannotBeAttached()
    {
        _db.Users.Add(new User { Id = "usr-9", Email = "x@x.com", Name = "Contractor", PasswordHash = "x" });
        _db.SaveChanges();
        AddClaim("clm-1", "usr-9");

        var row = Assert.Single(await _service.GetAttachableAsync());

        Assert.NotNull(row.BlockedReason);
        Assert.Null(row.EmployeeProfileId);
    }

    // ─── Fakes ──────────────────────────────────────────────────────────
}
