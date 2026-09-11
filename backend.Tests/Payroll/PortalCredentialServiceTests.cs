using AltomateHR.Api.Common;
using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Payroll;
using AltomateHR.Api.Modules.Payroll.Cron;
using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using AltomateHR.Api.Tests.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AltomateHR.Api.Tests.Payroll;

// Saved statutory-portal logins, and the daily leaver sweep.
//
// The password here is deliberately RECOVERABLE — the whole feature is
// showing it back to the admin at filing time. So what is pinned is that it
// is encrypted at rest, that reading it is a separate audited act, and that
// the list never leaks it.
public class PortalCredentialServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly StubCurrentUser _currentUser = new();
    private readonly FakeAuditService _audit = new();
    private readonly ISecretBox _secrets;
    private readonly PortalCredentialService _service;

    public PortalCredentialServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"portal-creds-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, _currentUser);

        _secrets = new SecretBox(
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Secrets:PortalCredentialsKey"] = "a-test-key",
                })
                .Build(),
            new StubEnvironment());

        _service = new PortalCredentialService(
            new PortalCredentialRepository(_db), _secrets, _audit);
    }

    public void Dispose() => _db.Dispose();

    private static SavePortalCredentialDto Save(
        string? loginId = "E1234567890",
        string? password = "hunter2",
        string? notes = null) => new()
        {
            LoginId = loginId,
            Password = password,
            Notes = notes,
        };

    // ─── Encryption at rest ─────────────────────────────────────────────

    // The stored column must not contain the password. That is the entire
    // reason this goes through SecretBox rather than a plain string.
    [Fact]
    public async Task ThePasswordIsNotStoredInClear()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save(password: "hunter2"));

        var stored = await _db.PayrollPortalCredentials.SingleAsync();

        Assert.NotNull(stored.PasswordEncrypted);
        Assert.DoesNotContain("hunter2", stored.PasswordEncrypted);
        Assert.Equal("hunter2", _secrets.Decrypt(stored.PasswordEncrypted));
    }

    // A fresh IV per call, so a reader of the raw table cannot tell two orgs
    // use the same password.
    [Fact]
    public async Task TheSamePasswordEncryptsDifferentlyEachTime()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save(password: "hunter2"));
        await _service.SaveAsync(PortalKind.PERKESO, Save(password: "hunter2"));

        var blobs = await _db.PayrollPortalCredentials
            .Select(c => c.PasswordEncrypted).ToListAsync();

        Assert.NotEqual(blobs[0], blobs[1]);
        Assert.All(blobs, b => Assert.Equal("hunter2", _secrets.Decrypt(b)));
    }

    // A blob written under a previous key reads as "no password" rather than
    // taking the credentials page down.
    [Fact]
    public void AnUndecryptableBlob_ReadsAsNothing()
    {
        Assert.Null(_secrets.Decrypt("not-base64-at-all"));
        Assert.Null(_secrets.Decrypt(Convert.ToBase64String(new byte[8])));
        Assert.Null(_secrets.Decrypt(null));
    }

    [Fact]
    public void EncryptingNothingProducesNothing()
    {
        Assert.Null(_secrets.Encrypt(null));
        Assert.Null(_secrets.Encrypt(string.Empty));
    }

    // ─── The list never leaks a password ────────────────────────────────

    [Fact]
    public async Task TheListMasksEveryPassword()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save());

        var saved = (await _service.GetAllAsync()).Single(c => c.Portal == PortalKind.KWSP);

        Assert.Null(saved.Password);
        // But it still says one exists, so the UI can show "saved".
        Assert.True(saved.HasPassword);
        Assert.Equal("E1234567890", saved.LoginId);
    }

    // One card per portal whether or not anything is saved, so the page
    // renders the same shape for a new org.
    [Fact]
    public async Task EveryPortalAppears_ConfiguredOrNot()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save());

        var all = await _service.GetAllAsync();

        Assert.Equal(Enum.GetValues<PortalKind>().Length, all.Count);
        Assert.True(all.Single(c => c.Portal == PortalKind.KWSP).IsConfigured);
        Assert.False(all.Single(c => c.Portal == PortalKind.PERKESO).IsConfigured);
    }

    // ─── Revealing is a separate, audited act ───────────────────────────

    [Fact]
    public async Task RevealingReturnsThePasswordAndIsAudited()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save(password: "hunter2"));

        var revealed = await _service.RevealAsync(PortalKind.KWSP);

        Assert.Equal("hunter2", revealed!.Password);
        Assert.True(_audit.Recorded("payroll.portal-credential.reveal"));
    }

    [Fact]
    public async Task RevealingSomethingUnsaved_IsNotFound()
    {
        Assert.Null(await _service.RevealAsync(PortalKind.LHDN));
    }

    // An audit log that records the password defeats encrypting it.
    [Fact]
    public async Task TheAuditTrailNeverContainsThePassword()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save(password: "hunter2"));
        await _service.RevealAsync(PortalKind.KWSP);

        var written = System.Text.Json.JsonSerializer.Serialize(_audit.Written);

        Assert.DoesNotContain("hunter2", written);
    }

    // ─── The three meanings of the password field ───────────────────────

    // Omitted means "leave it alone", so an admin can fix a typo in the
    // login id without retyping a password they may not have to hand.
    [Fact]
    public async Task OmittingThePassword_LeavesTheStoredOneAlone()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save(password: "hunter2"));
        await _service.SaveAsync(PortalKind.KWSP, Save(loginId: "E999", password: null));

        var revealed = await _service.RevealAsync(PortalKind.KWSP);

        Assert.Equal("E999", revealed!.LoginId);
        Assert.Equal("hunter2", revealed.Password);
    }

    [Fact]
    public async Task AnEmptyPassword_ClearsIt()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save(password: "hunter2"));
        await _service.SaveAsync(PortalKind.KWSP, Save(password: string.Empty));

        var revealed = await _service.RevealAsync(PortalKind.KWSP);

        Assert.Null(revealed!.Password);
        Assert.False(revealed.HasPassword);
    }

    [Fact]
    public async Task ANewPasswordReplacesTheOld()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save(password: "hunter2"));
        await _service.SaveAsync(PortalKind.KWSP, Save(password: "correct-horse"));

        Assert.Equal("correct-horse", (await _service.RevealAsync(PortalKind.KWSP))!.Password);
    }

    // ─── One row per portal ─────────────────────────────────────────────

    [Fact]
    public async Task SavingTwiceUpdatesInPlace()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save());
        await _service.SaveAsync(PortalKind.KWSP, Save(loginId: "E999"));

        Assert.Single(await _db.PayrollPortalCredentials.ToListAsync());
    }

    [Fact]
    public async Task DeletingRemovesIt()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save());

        Assert.True(await _service.DeleteAsync(PortalKind.KWSP));
        Assert.False(await _service.DeleteAsync(PortalKind.KWSP));
        Assert.False((await _service.GetAllAsync())
            .Single(c => c.Portal == PortalKind.KWSP).IsConfigured);
    }

    // ─── Tenant isolation ───────────────────────────────────────────────

    // Another org's saved credentials would be the worst possible leak here.
    [Fact]
    public async Task AnotherOrgSeesNoneOfOurs()
    {
        await _service.SaveAsync(PortalKind.KWSP, Save());

        _currentUser.OrganizationId = "org-2";
        Assert.False((await _service.GetAllAsync())
            .Single(c => c.Portal == PortalKind.KWSP).IsConfigured);
        Assert.Null(await _service.RevealAsync(PortalKind.KWSP));

        _currentUser.OrganizationId = "org-1";
        Assert.NotNull(await _service.RevealAsync(PortalKind.KWSP));
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}

// The daily sweep that archives departed staff.
//
// No financial effect — generation already skips a past-leave-date employee.
// This is bookkeeping, so the Active list does not fill with leavers.
public class PastLeaverArchiverTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly PastLeaverArchiver _archiver;

    public PastLeaverArchiverTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"past-leavers-{Guid.NewGuid()}")
            .Options;
        _db = new AppDbContext(options, new StubCurrentUser());
        _archiver = new PastLeaverArchiver(new EmployeeProfileRepository(_db));
    }

    public void Dispose() => _db.Dispose();

    private void Add(string id, DateTime? leaveDate, bool archived = false, string org = "org-1")
    {
        _db.EmployeeProfiles.Add(new EmployeeProfile
        {
            Id = id,
            OrganizationId = org,
            UserId = $"usr-{id}",
            LeaveDate = leaveDate,
            IsArchived = archived,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task APastLeaverIsArchived()
    {
        Add("emp-1", DateTime.UtcNow.Date.AddDays(-1));

        Assert.Equal(1, await _archiver.SweepAsync(100));

        var profile = await _db.EmployeeProfiles.SingleAsync();
        Assert.True(profile.IsArchived);
        Assert.NotNull(profile.ArchivedAt);
        Assert.Equal("Leave date passed", profile.ArchiveReason);
    }

    // Someone's last working day is a day they are still employed.
    [Fact]
    public async Task SomeoneLeavingTodayStaysActive()
    {
        Add("emp-1", DateTime.UtcNow.Date);

        Assert.Equal(0, await _archiver.SweepAsync(100));
    }

    // The case this exists for: a leave date set months ahead, on a profile
    // nobody reopens.
    [Fact]
    public async Task AFutureLeaverStaysActive()
    {
        Add("emp-1", DateTime.UtcNow.Date.AddMonths(3));

        Assert.Equal(0, await _archiver.SweepAsync(100));
    }

    [Fact]
    public async Task SomeoneWithNoLeaveDateIsUntouched()
    {
        Add("emp-1", null);

        Assert.Equal(0, await _archiver.SweepAsync(100));
    }

    // A second pass the same day must do nothing.
    [Fact]
    public async Task TheSweepIsIdempotent()
    {
        Add("emp-1", DateTime.UtcNow.Date.AddDays(-1));

        Assert.Equal(1, await _archiver.SweepAsync(100));
        Assert.Equal(0, await _archiver.SweepAsync(100));
    }

    // It runs with no request context, so one pass covers every org.
    [Fact]
    public async Task OnePassCoversEveryOrg()
    {
        Add("emp-1", DateTime.UtcNow.Date.AddDays(-1), org: "org-1");
        Add("emp-2", DateTime.UtcNow.Date.AddDays(-1), org: "org-2");

        Assert.Equal(2, await _archiver.SweepAsync(100));
    }

    // Capped, so one very large org cannot monopolise a sweep.
    [Fact]
    public async Task TheSweepIsCapped()
    {
        for (var i = 0; i < 5; i++) Add($"emp-{i}", DateTime.UtcNow.Date.AddDays(-1));

        Assert.Equal(2, await _archiver.SweepAsync(2));
    }
}
