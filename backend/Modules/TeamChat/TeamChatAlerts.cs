using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.TeamChat;

public class TeamChatAlerts : ITeamChatAlerts
{
    private readonly ITeamChatQueue _queue;
    private readonly IUserRepository _users;
    private readonly IOrganizationRepository _organizations;
    private readonly ICurrentUser _currentUser;
    private readonly TeamChatOptions _options;
    private readonly ILogger<TeamChatAlerts> _logger;

    public TeamChatAlerts(
        ITeamChatQueue queue,
        IUserRepository users,
        IOrganizationRepository organizations,
        ICurrentUser currentUser,
        IOptions<TeamChatOptions> options,
        ILogger<TeamChatAlerts> logger)
    {
        _queue = queue;
        _users = users;
        _organizations = organizations;
        _currentUser = currentUser;
        _options = options.Value;
        _logger = logger;
    }

    public async Task SignedInAsync(string userId, string role, string organizationId, string via)
    {
        if (!_options.IsConfigured) return;
        try
        {
            var user = await _users.GetByIdAsync(userId);
            var org = await _organizations.GetByIdAsync(organizationId);
            Enqueue(SignedInText(
                user?.Name, user?.Email ?? userId, role, org?.Name ?? organizationId, via, _currentUser.IpAddress));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Couldn't queue the sign-in alert for {UserId}", userId);
        }
    }

    public async Task CompanyCreatedAsync(string organizationId, string? ownerUserId, string via)
    {
        if (!_options.IsConfigured) return;
        try
        {
            var org = await _organizations.GetByIdAsync(organizationId);
            var owner = ownerUserId is null ? null : await _users.GetByIdAsync(ownerUserId);
            Enqueue(CompanyCreatedText(
                org?.Name ?? organizationId, owner?.Name, owner?.Email, _currentUser.Email, via));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Couldn't queue the new-company alert for {OrganizationId}", organizationId);
        }
    }

    public async Task CompanyStatusChangedAsync(string organizationId, bool deactivated, string? reason)
    {
        if (!_options.IsConfigured) return;
        try
        {
            var org = await _organizations.GetByIdAsync(organizationId);
            Enqueue(CompanyStatusText(org?.Name ?? organizationId, deactivated, reason, _currentUser.Email));
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Couldn't queue the company status alert for {OrganizationId}", organizationId);
        }
    }

    // ---- The messages ----

    public static string CompanyStatusText(string company, bool deactivated, string? reason, string? by)
    {
        var lines = new List<string>
        {
            deactivated ? $"⛔ Company deactivated: {Clean(company)}" : $"✅ Company reactivated: {Clean(company)}",
        };
        if (!string.IsNullOrWhiteSpace(reason))
            lines.Add(deactivated ? $"Reason: {Clean(reason)}" : $"Was deactivated for: {Clean(reason)}");
        if (!string.IsNullOrWhiteSpace(by)) lines.Add($"By: {Clean(by)}");
        return string.Join("\n", lines);
    }

    public static string SignedInText(
        string? name, string email, string role, string company, string via, string? ip) =>
        string.Join("\n",
            $"🔑 Sign-in: {Who(name, email)}",
            $"Company: {Clean(company)} · {Clean(role)}",
            $"Via: {via}{(string.IsNullOrWhiteSpace(ip) ? "" : $" · IP {ip}")}");

    public static string CompanyCreatedText(
        string company, string? ownerName, string? ownerEmail, string? createdBy, string via)
    {
        var lines = new List<string> { $"🏢 New company: {Clean(company)}" };
        if (ownerEmail is not null) lines.Add($"Owner: {Who(ownerName, ownerEmail)}");
        // Said only when someone else made it (Support making it for a customer).
        if (createdBy is not null && !string.Equals(createdBy, ownerEmail, StringComparison.OrdinalIgnoreCase))
            lines.Add($"Created by: {Clean(createdBy)}");
        lines.Add($"Via: {via}");
        return string.Join("\n", lines);
    }

    private static string Who(string? name, string email) =>
        string.IsNullOrWhiteSpace(name) || string.Equals(name, email, StringComparison.OrdinalIgnoreCase)
            ? Clean(email)
            : $"{Clean(name)} ({Clean(email)})";

    // Names and company names are typed by customers. Bitrix reads [B], [URL=…]
    // and the like as formatting, so brackets are softened to plain ones.
    private static string Clean(string text) =>
        text.Replace('[', '(').Replace(']', ')').ReplaceLineEndings(" ").Trim();

    private void Enqueue(string message)
    {
        if (!_queue.TryEnqueue(message))
            _logger.LogWarning("Team chat queue is full; an alert was dropped.");
    }
}
