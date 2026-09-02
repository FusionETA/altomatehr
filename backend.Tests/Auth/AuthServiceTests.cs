using AltomateHR.Api.Tests.Common;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Employees.Entities;
using AltomateHR.Api.Modules.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.RegularExpressions;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Tests.Auth;

public class AuthServiceTests
{
    [Fact]
    public async Task LoginAsync_WithWrongPassword_ReturnsNull()
    {
        var service = CreateService(
            users: [CreateUser(password: "correct-password")],
            refreshTokens: out _);

        var result = await service.LoginAsync("admin@altomate.com", "wrong-password");

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_WithNoMembership_ReturnsNull()
    {
        // Valid credentials, but the account belongs to no org yet → can't scope a token.
        var service = CreateService(
            users: [CreateUser(password: "correct-password")],
            refreshTokens: out _,
            memberships: []);

        var result = await service.LoginAsync("admin@altomate.com", "correct-password");

        Assert.Null(result);
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_IssuesTokensWithMembershipRoleAndOrg()
    {
        var service = CreateService(
            users: [CreateUser(password: "correct-password")],
            refreshTokens: out var refreshTokens,
            memberships: [Membership("usr-admin", "Admin", "org-1")]);

        var result = await service.LoginAsync("admin@altomate.com", "correct-password");

        Assert.NotNull(result);
        Assert.Equal("admin@altomate.com", result.Email);
        Assert.Equal("Admin", result.Role);              // role comes from the membership
        Assert.Equal("org-1", result.OrganizationId);    // active org from the membership
        Assert.Equal("access-token-1", result.AccessToken);
        Assert.Equal("refresh-token-1", result.RefreshToken);
        Assert.Single(refreshTokens.Tokens);
    }

    [Fact]
    public async Task LoginAsync_WithLegacyScryptPassword_Succeeds_AndUpgradesToBcrypt()
    {
        // A user migrated from the monolith carries a scrypt hash, not BCrypt.
        var user = new User
        {
            Id = "usr-admin",
            Email = "admin@altomate.com",
            PasswordHash = PasswordHasherTests.LegacyScryptHash,
            CreatedAt = DateTime.UtcNow,
        };
        var service = CreateService(
            users: [user],
            refreshTokens: out _,
            memberships: [Membership("usr-admin", "Admin", "org-1")]);

        var result = await service.LoginAsync("admin@altomate.com", PasswordHasherTests.LegacyPassword);

        Assert.NotNull(result);                        // the old-format password verifies
        Assert.StartsWith("$2", user.PasswordHash);    // and is transparently re-hashed to BCrypt
    }

    [Fact]
    public async Task SwitchOrgAsync_ToAMemberOrg_IssuesTokenForThatOrgAndRole()
    {
        var service = CreateService(
            users: [CreateUser(password: "x")],
            refreshTokens: out _,
            memberships:
            [
                Membership("usr-admin", "Admin", "org-1"),
                Membership("usr-admin", "Employee", "org-2"),   // just an employee here
            ]);

        var result = await service.SwitchOrgAsync("usr-admin", "org-2");

        Assert.NotNull(result);
        Assert.Equal("org-2", result.OrganizationId);
        Assert.Equal("Employee", result.Role);   // role is per-org
    }

    [Fact]
    public async Task SwitchOrgAsync_ToANonMemberOrg_ReturnsNull()
    {
        var service = CreateService(
            users: [CreateUser(password: "x")],
            refreshTokens: out _,
            memberships: [Membership("usr-admin", "Admin", "org-1")]);

        var result = await service.SwitchOrgAsync("usr-admin", "org-nope");

        Assert.Null(result);
    }

    [Fact]
    public async Task RefreshAsync_WithActiveRefreshToken_RotatesToken()
    {
        var existing = new RefreshToken
        {
            Token = "existing-refresh-token",
            UserId = "usr-admin",
            Email = "admin@altomate.com",
            Role = "Admin",
            OrganizationId = "org-1",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddDays(1),
        };
        var service = CreateService(
            users: [],
            refreshTokens: out var refreshTokens,
            existingRefreshTokens: [existing]);

        var result = await service.RefreshAsync("existing-refresh-token");

        Assert.NotNull(result);
        Assert.NotNull(existing.RevokedAt);
        Assert.Equal("access-token-1", result.AccessToken);
        Assert.Equal("refresh-token-1", result.RefreshToken);
        Assert.Equal(2, refreshTokens.Tokens.Count);
        Assert.Contains(refreshTokens.Tokens, t => t.Token == "refresh-token-1");
    }

    [Fact]
    public async Task RefreshAsync_WithRevokedToken_ReturnsNull()
    {
        var existing = new RefreshToken
        {
            Token = "revoked-refresh-token",
            UserId = "usr-admin",
            Email = "admin@altomate.com",
            Role = "Admin",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddDays(1),
            RevokedAt = DateTime.UtcNow,
        };
        var service = CreateService(
            users: [],
            refreshTokens: out _,
            existingRefreshTokens: [existing]);

        var result = await service.RefreshAsync("revoked-refresh-token");

        Assert.Null(result);
    }

    // --- password reset ---

    // The reset flow needs to look inside its collaborators — the code is only
    // ever emailed, and the point of most of these tests is what the repository
    // holds afterwards. So this returns the fakes alongside the service instead
    // of threading more out-params through CreateService.
    private sealed record ResetHarness(
        AuthService Service,
        FakeUserRepository Users,
        FakePasswordResetOtpRepository Otps,
        FakeEmailSender Email,
        FakeRefreshTokenRepository RefreshTokens);

    private static ResetHarness CreateResetHarness(
        IEnumerable<User> users,
        IEnumerable<RefreshToken>? existingRefreshTokens = null,
        bool emailSucceeds = true)
    {
        var userRepo = new FakeUserRepository(users);
        var otps = new FakePasswordResetOtpRepository();
        var email = new FakeEmailSender { Succeeds = emailSucceeds };
        var refreshTokens = new FakeRefreshTokenRepository(existingRefreshTokens ?? []);

        var service = new AuthService(
            tokens: new FakeTokenService(),
            refreshRepo: refreshTokens,
            userRepo: userRepo,
            directory: TestDirectory.Over(
                new FakeMembershipRepository([Membership("usr-admin", "Admin", "org-1")])),
            otpRepo: otps,
            email: email,
            logger: NullLogger<AuthService>.Instance,
            config: new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:RefreshTokenDays"] = "7",
                })
                .Build());

        return new ResetHarness(service, userRepo, otps, email, refreshTokens);
    }

    // The code never leaves the email, which is the point — a test that wants to
    // redeem one has to read it the way a user would.
    private static string CodeFromEmail(FakeEmailSender email) =>
        Regex.Match(email.Sent.Single().Body, @"\b(\d{6})\b").Groups[1].Value;

    [Fact]
    public async Task ForgotPassword_ForUnknownEmail_RecordsNothingAndSendsNothing()
    {
        // Any observable difference here turns the endpoint into an
        // account-existence oracle.
        var h = CreateResetHarness(users: []);

        await h.Service.ForgotPasswordAsync("nobody@altomate.com");

        Assert.Empty(h.Otps.Otps);
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task ForgotPassword_StoresAHashOfTheCode_NeverThePlaintext()
    {
        var h = CreateResetHarness([CreateUser("old-password")]);

        await h.Service.ForgotPasswordAsync("admin@altomate.com");

        var code = CodeFromEmail(h.Email);
        var stored = h.Otps.Otps.Single();
        Assert.NotEqual(code, stored.CodeHash);
        Assert.DoesNotContain(code, stored.CodeHash);
        Assert.True(BC.Verify(code, stored.CodeHash));
    }

    [Fact]
    public async Task ForgotPassword_Twice_LeavesOnlyTheNewestCodeUsable()
    {
        var h = CreateResetHarness([CreateUser("old-password")]);

        await h.Service.ForgotPasswordAsync("admin@altomate.com");
        var firstCode = CodeFromEmail(h.Email);
        h.Email.Sent.Clear();
        await h.Service.ForgotPasswordAsync("admin@altomate.com");

        // The first code must be dead even though its row still exists.
        Assert.Equal(2, h.Otps.Otps.Count);
        var error = await h.Service.ResetPasswordAsync("admin@altomate.com", firstCode, "new-password");
        Assert.NotNull(error);
    }

    [Fact]
    public async Task ForgotPassword_WhenTheEmailFails_DoesNotThrow()
    {
        // Surfacing a send failure would also leak that the account exists.
        var h = CreateResetHarness([CreateUser("old-password")], emailSucceeds: false);

        await h.Service.ForgotPasswordAsync("admin@altomate.com");

        Assert.Single(h.Otps.Otps);
    }

    [Fact]
    public async Task ResetPassword_WithTheEmailedCode_ChangesThePasswordAndConsumesIt()
    {
        var h = CreateResetHarness([CreateUser("old-password")]);
        await h.Service.ForgotPasswordAsync("admin@altomate.com");
        var code = CodeFromEmail(h.Email);

        var error = await h.Service.ResetPasswordAsync("admin@altomate.com", code, "new-password");

        Assert.Null(error);
        Assert.True(BC.Verify("new-password", h.Users.Users.Single().PasswordHash));
        Assert.NotNull(h.Otps.Otps.Single().ConsumedAt);
    }

    [Fact]
    public async Task ResetPassword_KillsEverySession()
    {
        // Otherwise a stolen refresh token survives the reset meant to lock the
        // attacker out — the whole reason the reset exists.
        var live = new RefreshToken
        {
            Token = "stolen-token",
            UserId = "usr-admin",
            Email = "admin@altomate.com",
            Role = "Admin",
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        var h = CreateResetHarness([CreateUser("old-password")], existingRefreshTokens: [live]);
        await h.Service.ForgotPasswordAsync("admin@altomate.com");

        await h.Service.ResetPasswordAsync("admin@altomate.com", CodeFromEmail(h.Email), "new-password");

        Assert.NotNull(h.RefreshTokens.Tokens.Single().RevokedAt);
    }

    [Fact]
    public async Task ResetPassword_WithAWrongCode_BurnsAnAttemptAndLeavesThePassword()
    {
        var h = CreateResetHarness([CreateUser("old-password")]);
        await h.Service.ForgotPasswordAsync("admin@altomate.com");

        var error = await h.Service.ResetPasswordAsync("admin@altomate.com", "000000", "new-password");

        Assert.NotNull(error);
        Assert.Equal(1, h.Otps.Otps.Single().AttemptCount);
        Assert.True(BC.Verify("old-password", h.Users.Users.Single().PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_AfterTooManyWrongGuesses_RefusesTheCorrectCode()
    {
        // A six-digit code is only a million guesses; the cap is what makes it
        // safe to email one.
        var h = CreateResetHarness([CreateUser("old-password")]);
        await h.Service.ForgotPasswordAsync("admin@altomate.com");
        var code = CodeFromEmail(h.Email);

        for (var i = 0; i < PasswordResetOtp.MaxAttempts; i++)
            await h.Service.ResetPasswordAsync("admin@altomate.com", "000000", "new-password");

        var error = await h.Service.ResetPasswordAsync("admin@altomate.com", code, "new-password");

        Assert.NotNull(error);
        Assert.True(BC.Verify("old-password", h.Users.Users.Single().PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_WithAnExpiredCode_Refuses()
    {
        var h = CreateResetHarness([CreateUser("old-password")]);
        await h.Service.ForgotPasswordAsync("admin@altomate.com");
        var code = CodeFromEmail(h.Email);
        h.Otps.Otps.Single().ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

        var error = await h.Service.ResetPasswordAsync("admin@altomate.com", code, "new-password");

        Assert.NotNull(error);
        Assert.True(BC.Verify("old-password", h.Users.Users.Single().PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_ReusingAConsumedCode_Refuses()
    {
        var h = CreateResetHarness([CreateUser("old-password")]);
        await h.Service.ForgotPasswordAsync("admin@altomate.com");
        var code = CodeFromEmail(h.Email);
        await h.Service.ResetPasswordAsync("admin@altomate.com", code, "first-new-password");

        var error = await h.Service.ResetPasswordAsync("admin@altomate.com", code, "second-new-password");

        Assert.NotNull(error);
        Assert.True(BC.Verify("first-new-password", h.Users.Users.Single().PasswordHash));
    }

    [Fact]
    public async Task ResetPassword_WithNoCodeRequested_Refuses()
    {
        var h = CreateResetHarness([CreateUser("old-password")]);

        var error = await h.Service.ResetPasswordAsync("admin@altomate.com", "123456", "new-password");

        Assert.NotNull(error);
    }

    // --- helpers ---

    private static AuthService CreateService(
        IEnumerable<User> users,
        out FakeRefreshTokenRepository refreshTokens,
        IEnumerable<RefreshToken>? existingRefreshTokens = null,
        IEnumerable<OrganizationMembership>? memberships = null)
    {
        refreshTokens = new FakeRefreshTokenRepository(existingRefreshTokens ?? []);

        return new AuthService(
            tokens: new FakeTokenService(),
            refreshRepo: refreshTokens,
            userRepo: new FakeUserRepository(users),
            directory: TestDirectory.Over(
                new FakeMembershipRepository(memberships ?? [Membership("usr-admin", "Admin", "org-1")])),
            otpRepo: new FakePasswordResetOtpRepository(),
            email: new FakeEmailSender(),
            logger: NullLogger<AuthService>.Instance,
            config: new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:RefreshTokenDays"] = "7",
                })
                .Build());
    }

    private static User CreateUser(string password) => new()
    {
        Id = "usr-admin",
        Email = "admin@altomate.com",
        PasswordHash = BC.HashPassword(password),
        CreatedAt = DateTime.UtcNow,
    };

    private static OrganizationMembership Membership(string userId, string role, string org) => new()
    {
        OrganizationId = org,
        UserId = userId,
        Role = role,
    };

    private sealed class FakeTokenService : ITokenService
    {
        private int _accessTokenCount;
        private int _refreshTokenCount;

        public string CreateToken(string userId, string email, string role, string organizationId) =>
            $"access-token-{++_accessTokenCount}";

        public string CreateRefreshToken() =>
            $"refresh-token-{++_refreshTokenCount}";
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly List<User> _users;

        // Exposed so a reset test can assert the stored hash actually changed.
        public IReadOnlyList<User> Users => _users;

        public FakeUserRepository(IEnumerable<User> users) => _users = users.ToList();

        public Task AddAsync(User user)
        {
            _users.Add(user);
            return Task.CompletedTask;
        }

        public Task<bool> AnyAsync() => Task.FromResult(_users.Count > 0);

        public Task<User?> GetByEmailAsync(string email) =>
            Task.FromResult(_users.FirstOrDefault(u =>
                string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase)));

        public Task<User?> GetByIdAsync(string id) =>
            Task.FromResult(_users.FirstOrDefault(u => u.Id == id));

        public Task<List<User>> GetAllAsync() => Task.FromResult(_users.ToList());

        public Task UpdateAsync(User user) => Task.CompletedTask;
    }

    private sealed class FakeMembershipRepository : IOrganizationMembershipRepository
    {
        private readonly List<OrganizationMembership> _m;
        public FakeMembershipRepository(IEnumerable<OrganizationMembership> m) => _m = m.ToList();

        public Task<List<OrganizationMembership>> GetByUserAsync(string userId) =>
            Task.FromResult(_m.Where(x => x.UserId == userId).ToList());
        public Task<OrganizationMembership?> GetAsync(string organizationId, string userId) =>
            Task.FromResult(_m.FirstOrDefault(x => x.OrganizationId == organizationId && x.UserId == userId));
        public Task<List<OrganizationMembership>> GetForCurrentOrgAsync() => Task.FromResult(_m.ToList());
        public Task<OrganizationMembership?> GetForUserInCurrentOrgAsync(string userId) =>
            Task.FromResult(_m.FirstOrDefault(x => x.UserId == userId));
        public Task<List<OrganizationMembership>> GetBySupervisorAsync(string supervisorId) =>
            Task.FromResult(_m.Where(x => x.SupervisorId == supervisorId).ToList());
        public Task<int> CountByShiftIdAsync(string shiftId) =>
            Task.FromResult(_m.Count(x => x.ShiftId == shiftId));
        public Task AddAsync(OrganizationMembership m) { _m.Add(m); return Task.CompletedTask; }
        public Task UpdateAsync(OrganizationMembership m) => Task.CompletedTask;
    }

    // Password-reset codes aren't exercised by the tests here yet; these exist so
    // AuthService can be constructed. Both keep their state, so a reset test can
    // assert against them when one is written.
    private sealed class FakePasswordResetOtpRepository : IPasswordResetOtpRepository
    {
        public List<PasswordResetOtp> Otps { get; } = [];

        public Task AddAsync(PasswordResetOtp otp)
        {
            Otps.Add(otp);
            return Task.CompletedTask;
        }

        // Mirrors PasswordResetOtpRepository exactly — consumed, attempt-capped and
        // expired rows are all invisible, newest first. A looser fake here would
        // make the expiry and attempt-cap tests pass without proving anything.
        public Task<PasswordResetOtp?> GetActiveByEmailAsync(string email) =>
            Task.FromResult(
                Otps.Where(o => o.Email == email
                                && o.ConsumedAt == null
                                && o.AttemptCount < PasswordResetOtp.MaxAttempts
                                && o.ExpiresAt > DateTime.UtcNow)
                    .OrderByDescending(o => o.CreatedAt)
                    .FirstOrDefault());

        public Task UpdateAsync(PasswordResetOtp otp) => Task.CompletedTask;

        public Task InvalidateAllForUserAsync(string userId)
        {
            foreach (var otp in Otps.Where(o => o.UserId == userId && o.ConsumedAt == null))
                otp.ConsumedAt = DateTime.UtcNow;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];
        public bool Succeeds { get; init; } = true;

        public Task<bool> SendAsync(
            string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            Sent.Add((toEmail, subject, htmlBody));
            return Task.FromResult(Succeeds);
        }
    }

    private sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
    {
        public List<RefreshToken> Tokens { get; }

        public FakeRefreshTokenRepository(IEnumerable<RefreshToken> tokens) =>
            Tokens = tokens.ToList();

        public Task AddAsync(RefreshToken token)
        {
            Tokens.Add(token);
            return Task.CompletedTask;
        }

        public Task<RefreshToken?> GetByTokenAsync(string token) =>
            Task.FromResult(Tokens.FirstOrDefault(t => t.Token == token));

        public Task UpdateAsync(RefreshToken token) => Task.CompletedTask;

        // Mirrors RefreshTokenRepository: stamps every live token for the user,
        // leaving already-revoked ones alone. Behaves rather than stubs, so a
        // test can assert a password reset actually killed the sessions.
        public Task RevokeAllForUserAsync(string userId)
        {
            var now = DateTime.UtcNow;
            foreach (var token in Tokens.Where(t => t.UserId == userId && t.RevokedAt == null))
                token.RevokedAt = now;
            return Task.CompletedTask;
        }
    }
}
