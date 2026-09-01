using System.Security.Cryptography;
using AltomateHR.Api.Modules.Auth.Entities;
using AltomateHR.Api.Modules.Email;
using AltomateHR.Api.Modules.Employees;
using BC = BCrypt.Net.BCrypt;

namespace AltomateHR.Api.Modules.Auth;

// Orchestration: validate credentials (against hashed passwords), resolve which org
// the account is acting in (via its memberships), mint tokens, store/rotate/revoke
// refresh tokens. The token carries the ACTIVE org + the role for that org.
public class AuthService : IAuthService
{
    private readonly ITokenService _tokens;
    private readonly IRefreshTokenRepository _refreshRepo;
    private readonly IUserRepository _userRepo;
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IPasswordResetOtpRepository _otpRepo;
    private readonly IEmailSender _email;
    private readonly ILogger<AuthService> _logger;
    private readonly int _refreshDays;

    public AuthService(
        ITokenService tokens,
        IRefreshTokenRepository refreshRepo,
        IUserRepository userRepo,
        IOrganizationMembershipRepository memberships,
        IPasswordResetOtpRepository otpRepo,
        IEmailSender email,
        ILogger<AuthService> logger,
        IConfiguration config)
    {
        _tokens = tokens;
        _refreshRepo = refreshRepo;
        _userRepo = userRepo;
        _memberships = memberships;
        _otpRepo = otpRepo;
        _email = email;
        _logger = logger;
        _refreshDays = int.Parse(config["Jwt:RefreshTokenDays"] ?? "7");
    }

    public async Task<AuthResult?> LoginAsync(string email, string password)
    {
        var user = await _userRepo.GetByEmailAsync(email);

        // BC.Verify re-hashes `password` with the salt in user.PasswordHash and compares.
        if (user is null || !BC.Verify(password, user.PasswordHash))
            return null;

        // Log the account into its default (first) org. Role comes from that membership.
        var memberships = await _memberships.GetByUserAsync(user.Id);
        var active = memberships.FirstOrDefault();
        if (active is null)
            return null;   // valid credentials, but not a member of any org yet

        return await IssueTokensAsync(user.Id, user.Email, active.Role, active.OrganizationId);
    }

    public async Task<AuthResult?> SwitchOrgAsync(string userId, string organizationId)
    {
        // Only if the account is actually a member of the target org.
        var membership = await _memberships.GetAsync(organizationId, userId);
        if (membership is null) return null;

        var user = await _userRepo.GetByIdAsync(userId);
        if (user is null) return null;

        return await IssueTokensAsync(userId, user.Email, membership.Role, organizationId);
    }

    public async Task<IReadOnlyList<UserOrgDto>> GetOrgsAsync(string userId) =>
        (await _memberships.GetByUserAsync(userId))
            .Select(m => new UserOrgDto(m.OrganizationId, m.Role))
            .ToList();

    public async Task<AuthResult?> RefreshAsync(string refreshToken)
    {
        var stored = await _refreshRepo.GetByTokenAsync(refreshToken);
        if (stored is null || !stored.IsActive)
            return null;

        // Rotate: revoke the used token, then issue a fresh pair for the SAME active org.
        stored.RevokedAt = DateTime.UtcNow;
        await _refreshRepo.UpdateAsync(stored);

        return await IssueTokensAsync(stored.UserId, stored.Email, stored.Role, stored.OrganizationId);
    }

    public async Task LogoutAsync(string refreshToken)
    {
        var stored = await _refreshRepo.GetByTokenAsync(refreshToken);
        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = DateTime.UtcNow;
            await _refreshRepo.UpdateAsync(stored);
        }
    }

    // How long a reset code stays valid. Short by design: the code is only six
    // digits, so its security rests on the expiry and the attempt cap.
    private static readonly TimeSpan OtpLifetime = TimeSpan.FromMinutes(10);

    public async Task ForgotPasswordAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await _userRepo.GetByEmailAsync(email);

        // Unknown address: do nothing, but let the caller return 200 anyway.
        // Any difference in behaviour here — including timing or an error —
        // would turn this endpoint into an account-existence oracle.
        if (user is null)
        {
            _logger.LogInformation("Password reset requested for an unknown address.");
            return;
        }

        // Only one live code per user: requesting a new one kills the old.
        await _otpRepo.InvalidateAllForUserAsync(user.Id);

        var code = GenerateOtp();
        var now = DateTime.UtcNow;

        await _otpRepo.AddAsync(new PasswordResetOtp
        {
            UserId = user.Id,
            Email = user.Email,
            CodeHash = BC.HashPassword(code),
            ExpiresAt = now.Add(OtpLifetime),
            CreatedAt = now,
        });

        var minutes = (int)OtpLifetime.TotalMinutes;
        var sent = await _email.SendAsync(
            user.Email,
            "Your AltomateHR password reset code",
            BuildOtpEmail(user.Name, code, minutes),
            cancellationToken);

        // A send failure is logged, not surfaced — see the enumeration note above.
        if (!sent)
            _logger.LogError("Failed to send the password reset email for user {UserId}.", user.Id);
    }

    public async Task<string?> ResetPasswordAsync(string email, string otp, string newPassword)
    {
        const string invalid = "That code is invalid or has expired. Request a new one.";

        var record = await _otpRepo.GetActiveByEmailAsync(email);
        if (record is null) return invalid;

        if (!BC.Verify(otp, record.CodeHash))
        {
            // Burn an attempt so the six-digit space can't be walked.
            record.AttemptCount += 1;
            await _otpRepo.UpdateAsync(record);
            return invalid;
        }

        var user = await _userRepo.GetByIdAsync(record.UserId);
        if (user is null) return invalid;

        user.PasswordHash = BC.HashPassword(newPassword);
        await _userRepo.UpdateAsync(user);

        record.ConsumedAt = DateTime.UtcNow;
        await _otpRepo.UpdateAsync(record);

        // The password changed, so every existing session must die — otherwise a
        // stolen refresh token survives the very reset meant to lock the attacker out.
        await _refreshRepo.RevokeAllForUserAsync(user.Id);

        return null;
    }

    // Uniform across the full 000000-999999 range; RandomNumberGenerator avoids
    // the modulo bias a naive Random.Next would introduce.
    private static string GenerateOtp() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string BuildOtpEmail(string name, string code, int minutes)
    {
        var greeting = string.IsNullOrWhiteSpace(name) ? "Hello," : $"Hello {System.Net.WebUtility.HtmlEncode(name)},";
        return $"""
            <p>{greeting}</p>
            <p>Use this code to reset your AltomateHR password:</p>
            <p style="font-size:28px;font-weight:bold;letter-spacing:4px;">{code}</p>
            <p>It expires in {minutes} minutes and can only be used once.</p>
            <p>If you didn't request this, you can ignore this email — your password won't change.</p>
            """;
    }

    private async Task<AuthResult> IssueTokensAsync(string userId, string email, string role, string organizationId)
    {
        var accessToken = _tokens.CreateToken(userId, email, role, organizationId);

        var refresh = new RefreshToken
        {
            Token = _tokens.CreateRefreshToken(),
            UserId = userId,
            Email = email,
            Role = role,
            OrganizationId = organizationId,
            ExpiresAt = DateTime.UtcNow.AddDays(_refreshDays),
            CreatedAt = DateTime.UtcNow,
        };
        await _refreshRepo.AddAsync(refresh);

        return new AuthResult(accessToken, email, role, organizationId, refresh.Token, refresh.ExpiresAt);
    }
}
