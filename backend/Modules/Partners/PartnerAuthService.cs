using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Email;
using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Notifications;
using AltomateHR.Api.Modules.Notifications.Entities;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Partners.Dtos;
using AltomateHR.Api.Modules.Partners.Entities;

namespace AltomateHR.Api.Modules.Partners;

// Orchestrates the partner handshake: identify the app by its client secret,
// spend a single-use ticket, and mint short-lived scoped tokens. Talks to the
// registry (MySQL) + the ephemeral store (Redis); never to Prisma/EF directly.
public class PartnerAuthService : IPartnerAuthService
{
    private static readonly TimeSpan TicketTtl  = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan AccessTtl  = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefreshTtl = TimeSpan.FromHours(8);

    private readonly IDirectoryService _directory;
    private readonly IApiClientRepository _clients;
    private readonly IPartnerAuthStore _store;
    private readonly IOrganizationService _organizations;
    private readonly INotificationService _notifications;
    private readonly IEmailSender _email;

    public PartnerAuthService(
        IApiClientRepository clients,
        IPartnerAuthStore store,
        IDirectoryService directory,
        IOrganizationService organizations,
        INotificationService notifications,
        IEmailSender email)
    {
        _clients = clients;
        _store = store;
        _directory = directory;
        _organizations = organizations;
        _notifications = notifications;
        _email = email;
    }

    public async Task<string?> MintLaunchTicketAsync(string appName, string userId, string organizationId)
    {
        var client = await _clients.GetByNameAsync(appName);
        if (client is null || !client.Active) return null;

        var ticket = await _store.MintTicketAsync(
            new PartnerTicketData(client.Id, userId, organizationId), TicketTtl);

        var sep = client.RedirectUrl.Contains('?') ? '&' : '?';
        return $"{client.RedirectUrl}{sep}t={Uri.EscapeDataString(ticket)}";
    }

    public async Task<PartnerTokenResponseDto?> RedeemTicketAsync(string clientSecret, string ticket)
    {
        var client = await AuthenticateClientAsync(clientSecret);
        if (client is null) return null;

        var data = await _store.RedeemTicketAsync(ticket);
        if (data is null || data.ClientId != client.Id) return null;   // expired, or another app's ticket

        return await IssueAsync(client, data.UserId, data.OrganizationId);
    }

    public async Task<PartnerTokenResponseDto?> RefreshAsync(string clientSecret, string refreshToken)
    {
        var client = await AuthenticateClientAsync(clientSecret);
        if (client is null) return null;

        var data = await _store.RedeemRefreshTokenAsync(refreshToken);   // single-use → rotates
        if (data is null || data.ClientId != client.Id) return null;

        return await IssueAsync(client, data.UserId, data.OrganizationId);
    }

    public async Task<SendPartnerNotificationResponseDto?> SendNotificationAsync(
        string clientSecret, SendPartnerNotificationDto dto)
    {
        var client = await AuthenticateClientAsync(clientSecret);
        if (client is null) return null;   // → 401, same as the token endpoints

        if (!ApiScopes.Split(client.Scopes).Contains("notifications:write"))
            return new SendPartnerNotificationResponseDto { Status = PartnerNotificationStatus.Forbidden };

        // GetMembershipAsync is the same "does this user actually belong to this
        // org" check IssueAsync relies on below — null covers both an unknown
        // user id and a real user who isn't a member of the claimed org, and
        // either way the caller gets the same answer.
        var membership = await _directory.GetMembershipAsync(dto.OrganizationId, dto.UserId);
        if (membership is null)
            return new SendPartnerNotificationResponseDto { Status = PartnerNotificationStatus.UserNotFound };

        string? notificationId = null;
        var delivered = false;

        if (dto.Channel is NotificationChannel.NotificationCenter or NotificationChannel.Both)
        {
            notificationId = await _notifications.NotifyAsync(
                dto.OrganizationId, dto.UserId, NotificationType.PARTNER_MESSAGE, dto.Title, dto.Message, dto.Link);
            delivered = notificationId is not null;
        }

        if (dto.Channel is NotificationChannel.Email or NotificationChannel.Both)
        {
            var user = await _directory.GetUserAsync(dto.UserId);
            if (!string.IsNullOrEmpty(user?.Email))
            {
                // Best-effort, like every other email send in this codebase
                // (see AuthService) — a bounced/rejected send doesn't flip an
                // otherwise-successful NotificationCenter write to Failed.
                var sent = await _email.SendAsync(user.Email, dto.Title, BuildEmailBody(dto));
                delivered = delivered || sent;
            }
        }

        return delivered
            ? new SendPartnerNotificationResponseDto { Status = PartnerNotificationStatus.Success, NotificationId = notificationId }
            : new SendPartnerNotificationResponseDto { Status = PartnerNotificationStatus.Failed };
    }

    // Generic on purpose — this endpoint serves every registered partner app
    // (see PartnerAuthController), not just Appraisify.
    private static string BuildEmailBody(SendPartnerNotificationDto dto)
    {
        var body = $"<p>{System.Net.WebUtility.HtmlEncode(dto.Message)}</p>";
        if (!string.IsNullOrEmpty(dto.Link))
            body += $"<p><a href=\"{System.Net.WebUtility.HtmlEncode(dto.Link)}\">View details</a></p>";
        return body;
    }

    // Hash the presented secret and look the app up. Vague on failure by design.
    private async Task<ApiClient?> AuthenticateClientAsync(string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientSecret)) return null;
        var client = await _clients.GetBySecretHashAsync(PartnerTokenGenerator.Hash(clientSecret.Trim()));
        return client is { Active: true } ? client : null;
    }

    // Mint a fresh access + refresh pair bound to the ticket's org and the app's
    // granted scopes, and return the identity the partner needs.
    private async Task<PartnerTokenResponseDto> IssueAsync(ApiClient client, string userId, string orgId)
    {
        var tokenData = new PartnerTokenData(client.Id, userId, orgId, client.Scopes, client.Audience);

        var access = PartnerTokenGenerator.NewAccessToken();
        await _store.StoreAccessTokenAsync(access, tokenData, AccessTtl);

        var refresh = PartnerTokenGenerator.NewRefreshToken();
        await _store.StoreRefreshTokenAsync(refresh, tokenData, RefreshTtl);

        var user = await _directory.GetUserAsync(userId);
        var membership = await _directory.GetMembershipAsync(orgId, userId);   // explicit org → filter-safe off-request
        var org = await _organizations.GetByIdAsync(orgId);

        return new PartnerTokenResponseDto
        {
            AccessToken = access,
            RefreshToken = refresh,
            ExpiresIn = (int)AccessTtl.TotalSeconds,
            User = new PartnerUserDto
            {
                Id = userId,
                Email = user?.Email ?? string.Empty,
                Role = membership?.Role ?? string.Empty,
            },
            Organization = new PartnerOrgDto
            {
                Id = orgId,
                Name = org?.Name ?? string.Empty,
            },
        };
    }
}
