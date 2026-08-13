using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Auth.Entities;
using Microsoft.Extensions.Configuration;
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
    public async Task LoginAsync_WithValidCredentials_IssuesAccessAndRefreshTokens()
    {
        var service = CreateService(
            users: [CreateUser(password: "correct-password")],
            refreshTokens: out var refreshTokens);

        var result = await service.LoginAsync("admin@altomate.com", "correct-password");

        Assert.NotNull(result);
        Assert.Equal("admin@altomate.com", result.Email);
        Assert.Equal("Admin", result.Role);
        Assert.Equal("access-token-1", result.AccessToken);
        Assert.Equal("refresh-token-1", result.RefreshToken);
        Assert.Single(refreshTokens.Tokens);
        Assert.Equal("refresh-token-1", refreshTokens.Tokens[0].Token);
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

    private static AuthService CreateService(
        IEnumerable<User> users,
        out FakeRefreshTokenRepository refreshTokens,
        IEnumerable<RefreshToken>? existingRefreshTokens = null)
    {
        refreshTokens = new FakeRefreshTokenRepository(existingRefreshTokens ?? []);

        return new AuthService(
            tokens: new FakeTokenService(),
            refreshRepo: refreshTokens,
            userRepo: new FakeUserRepository(users),
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
        Role = "Admin",
        CreatedAt = DateTime.UtcNow,
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

        public Task<List<User>> GetBySupervisorAsync(string supervisorId) =>
            Task.FromResult(_users.Where(u => u.SupervisorId == supervisorId).ToList());

        public Task UpdateAsync(User user) => Task.CompletedTask;
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
    }
}
