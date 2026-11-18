using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Entities;
using AltomateHR.Api.Modules.Support;
using AltomateHR.Api.Modules.Support.Dtos;
using AltomateHR.Api.Tests.Audit;
using AltomateHR.Api.Tests.TeamChat;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Tests.SupportMode;

// Fusioneta support lifting a sign-in lock or block for someone who really
// forgot their password — seen from the support admin's own company.
public class SupportLoginBlockServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly LoginGuard _guard = new();
    private readonly FakeAuditService _audit = new();
    private readonly RecordingTeamChat _chat = new();
    private readonly SupportLoginBlockService _service;

    public SupportLoginBlockServiceTests()
    {
        var currentUser = new StubCurrentUser { UserId = "u-support", OrganizationId = "org-home", Email = "zr@fusioneta.test" };
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"login-blocks-{Guid.NewGuid()}").Options, currentUser);

        var users = new UserRepository(_db);
        _service = new SupportLoginBlockService(
            _guard,
            users,
            new DirectoryService(new OrganizationMembershipRepository(_db), new EmployeeProfileRepository(_db), users),
            new OrganizationRepository(_db),
            _audit,
            currentUser,
            _chat);

        _db.Organizations.AddRange(
            new Organization { Id = "org-home", Name = "Fusioneta", CreatedAt = DateTime.UtcNow },
            new Organization { Id = "org-a", Name = "Alpha Sdn Bhd", CreatedAt = DateTime.UtcNow });
        _db.Users.Add(new User { Id = "u-1", Email = "ali@alpha.example", Name = "Ali" });
        _db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = "org-a", UserId = "u-1", Role = "Employee" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private void LockAli()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < LoginGuard.AccountThreshold; i++)
            _guard.Fail(_guard.Begin(LoginGuard.UserKey("u-1"), "ali@alpha.example", null, now), now);
    }

    private void BlockIp(string ip)
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < LoginGuard.IpThreshold; i++)
            _guard.Fail(_guard.Begin(LoginGuard.EmailKey($"guess{i}@x.test"), $"guess{i}@x.test", ip, now), now);
    }

    // A company outside the support admin's own: the membership lookup
    // can't be narrowed to org-home, or the company would be missing.
    [Fact]
    public async Task ALockedAccount_IsListedWithItsPersonAndCompany()
    {
        LockAli();

        var row = Assert.Single(await _service.ListAsync());

        Assert.Equal(LoginBlockKind.ACCOUNT, row.Kind);
        Assert.Equal(("Ali", "ali@alpha.example", "Alpha Sdn Bhd"), (row.Name, row.Email, row.Company));
        Assert.True(row.UntilUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task UnlockingAnAccount_LetsThemIn_AndIsRecordedInTheirCompanysLogAndTheChat()
    {
        LockAli();
        var key = Assert.Single(await _service.ListAsync()).Key;

        Assert.True(await _service.UnblockAsync(LoginBlockKind.ACCOUNT, key));

        Assert.Empty(await _service.ListAsync());
        Assert.Null(_guard.Begin(LoginGuard.UserKey("u-1"), "ali@alpha.example", null, DateTime.UtcNow).Block);

        var entry = Assert.Single(_audit.Written);
        Assert.Equal(AuditActions.SupportLoginUnblock, entry.Action);
        Assert.Equal("org-a", entry.OrganizationId);
        Assert.Equal(("User", "u-1"), (entry.TargetType, entry.TargetId));
        // Shown to Alpha as "System (Support)"; the real person kept internally.
        Assert.Equal("u-support", entry.Support?.UserId);
        Assert.Contains("Ali (ali@alpha.example)", entry.Summary);

        Assert.Equal(["login-unblocked account Ali (ali@alpha.example) org-a"], _chat.Said);
    }

    // An IP belongs to no company. Never the session's company: a superadmin
    // inside a customer (support mode) would otherwise write someone else's IP
    // into that customer's log.
    [Fact]
    public async Task UnblockingAnIp_IsRecordedInTheInternalLogOnly()
    {
        BlockIp("203.0.113.9");
        var row = Assert.Single(await _service.ListAsync());
        Assert.Equal(("203.0.113.9", 10), (row.Ip, row.EmailsTried.Count));

        Assert.True(await _service.UnblockAsync(LoginBlockKind.IP, row.Key));

        Assert.Empty(_audit.Written);
        var entry = Assert.Single(_audit.WrittenInternally);
        Assert.Equal("u-support", entry.Support?.UserId);
        Assert.Equal(("IpAddress", "203.0.113.9"), (entry.TargetType, entry.TargetId));
        Assert.Equal(["login-unblocked ip 203.0.113.9 "], _chat.Said);
    }

    // Already run out, lifted by someone else, or a key that was never blocked:
    // nothing to record.
    [Fact]
    public async Task NothingInForce_IsFalse_AndRecordsNothing()
    {
        LockAli();
        var key = Assert.Single(await _service.ListAsync()).Key;
        Assert.True(await _service.UnblockAsync(LoginBlockKind.ACCOUNT, key));

        Assert.False(await _service.UnblockAsync(LoginBlockKind.ACCOUNT, key));
        Assert.False(await _service.UnblockAsync(LoginBlockKind.IP, key));
        Assert.False(await _service.UnblockAsync(LoginBlockKind.IP, "198.51.100.1"));

        Assert.Single(_audit.Written);
        Assert.Single(_chat.Said);
    }

    // An address no account has belongs to no company either.
    [Fact]
    public async Task UnlockingAnUnknownEmail_IsRecordedInTheInternalLogOnly()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < LoginGuard.AccountThreshold; i++)
            _guard.Fail(_guard.Begin(LoginGuard.EmailKey("bob@other.example"), "bob@other.example", null, now), now);
        var row = Assert.Single(await _service.ListAsync());
        Assert.Null(row.Name);

        Assert.True(await _service.UnblockAsync(LoginBlockKind.ACCOUNT, row.Key));

        Assert.Empty(_audit.Written);
        Assert.Contains("bob@other.example", Assert.Single(_audit.WrittenInternally).Summary);
    }
}
