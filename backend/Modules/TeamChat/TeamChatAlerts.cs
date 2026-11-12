using System.Globalization;
using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.TeamChat;

public class TeamChatAlerts : ITeamChatAlerts
{
    private static readonly TimeZoneInfo Myt = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kuala_Lumpur");

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

    // ---- Action chat ----

    public Task CompanyCreatedAsync(string organizationId, string? ownerUserId, string via) =>
        SayAsync(TeamChatChannel.Action, "new-company", async () =>
        {
            var owner = ownerUserId is null ? null : await _users.GetByIdAsync(ownerUserId);
            return CompanyCreatedText(
                await CompanyNameAsync(organizationId), owner?.Name, owner?.Email, _currentUser.Email, via);
        });

    public Task CompanyStatusChangedAsync(string organizationId, bool deactivated, string? reason) =>
        SayAsync(TeamChatChannel.Action, "company-status", async () =>
            CompanyStatusText(await CompanyNameAsync(organizationId), deactivated, reason, _currentUser.Email));

    public Task PayrollRunApprovedAsync(
        string organizationId, string runLabel, int people, decimal totalNet, bool firstEver) =>
        SayAsync(TeamChatChannel.Action, "payroll-approved", async () =>
            PayrollApprovedText(await CompanyNameAsync(organizationId), runLabel, people, totalNet, firstEver, _currentUser.Email));

    public Task LeaveDecidedAsync(
        string organizationId, string employeeUserId, string leaveType, double days,
        DateTime start, DateTime end, bool approved, bool byAdmin) =>
        SayAsync(TeamChatChannel.Action, "leave-decided", async () =>
        {
            var employee = await _users.GetByIdAsync(employeeUserId);
            return LeaveDecidedText(
                await CompanyNameAsync(organizationId), employee?.Name, employee?.Email ?? employeeUserId,
                leaveType, days, start, end, approved, byAdmin, _currentUser.Email);
        });

    public Task ClaimDecidedAsync(
        string organizationId, string employeeUserId, string title, string currency, decimal amount, bool approved) =>
        SayAsync(TeamChatChannel.Action, "claim-decided", async () =>
        {
            var employee = await _users.GetByIdAsync(employeeUserId);
            return ClaimDecidedText(
                await CompanyNameAsync(organizationId), employee?.Name, employee?.Email ?? employeeUserId,
                title, currency, amount, approved, _currentUser.Email);
        });

    public Task AttendanceRejectedAsync(
        string organizationId, string employeeUserId, string what, DateTime eventAtUtc, string? reason) =>
        SayAsync(TeamChatChannel.Action, "attendance-rejected", async () =>
        {
            var employee = await _users.GetByIdAsync(employeeUserId);
            return AttendanceRejectedText(
                await CompanyNameAsync(organizationId), employee?.Name, employee?.Email ?? employeeUserId,
                what, eventAtUtc, reason, _currentUser.Email);
        });

    // ---- Alert chat ----

    public Task SignedInAsync(string userId, string role, string organizationId, string via) =>
        SayAsync(TeamChatChannel.Alert, "sign-in", async () =>
        {
            var user = await _users.GetByIdAsync(userId);
            return SignedInText(
                user?.Name, user?.Email ?? userId, role, await CompanyNameAsync(organizationId), via, _currentUser.IpAddress);
        });

    public Task AccountLockedAsync(string email, string? userId, string? organizationId, TimeSpan duration, string? ip) =>
        SayAsync(TeamChatChannel.Alert, "account-locked", async () =>
        {
            var user = userId is null ? null : await _users.GetByIdAsync(userId);
            var company = organizationId is null ? null : await CompanyNameAsync(organizationId);
            return AccountLockedText(user?.Name, email, user is not null, company, duration, ip);
        });

    public Task IpBlockedAsync(string ip, TimeSpan duration, bool againToday, IReadOnlyList<string> emailsTried) =>
        SayAsync(TeamChatChannel.Alert, "ip-blocked", () =>
            Task.FromResult(IpBlockedText(ip, duration, againToday, emailsTried)));

    public Task PortalPasswordViewedAsync(string organizationId, string portal) =>
        SayAsync(TeamChatChannel.Alert, "portal-password", async () =>
            PortalPasswordViewedText(await CompanyNameAsync(organizationId), portal, _currentUser.Email, _currentUser.IpAddress));

    public Task XeroReconnectRequiredAsync(string organizationId, string? xeroOrganisation, string reason) =>
        SayAsync(TeamChatChannel.Alert, "xero-reconnect", async () =>
            XeroReconnectText(await CompanyNameAsync(organizationId), xeroOrganisation, reason));

    public Task PayslipEmailsFailedAsync(
        string organizationId, string runLabel, int total, IReadOnlyList<string> reasons) =>
        reasons.Count == 0
            ? Task.CompletedTask
            : SayAsync(TeamChatChannel.Alert, "payslip-emails", async () =>
                PayslipEmailsFailedText(await CompanyNameAsync(organizationId), runLabel, total, reasons, _currentUser.Email));

    public Task PayrollRunDeletedAsync(string organizationId, string runLabel) =>
        SayAsync(TeamChatChannel.Alert, "payroll-deleted", async () =>
            PayrollDeletedText(await CompanyNameAsync(organizationId), runLabel, _currentUser.Email));

    public Task PayrollRunRevertedAsync(string organizationId, string runLabel, IReadOnlyList<string> alsoReverted) =>
        SayAsync(TeamChatChannel.Alert, "payroll-reverted", async () =>
            PayrollRevertedText(await CompanyNameAsync(organizationId), runLabel, alsoReverted, _currentUser.Email));

    // ---- The messages ----

    public static string CompanyStatusText(string company, bool deactivated, string? reason, string? by)
    {
        var lines = new List<string>
        {
            deactivated ? $"⛔ Company deactivated: {Clean(company)}" : $"✅ Company reactivated: {Clean(company)}",
        };
        if (!string.IsNullOrWhiteSpace(reason))
            lines.Add(deactivated ? $"Reason: {Clean(reason)}" : $"Was deactivated for: {Clean(reason)}");
        AddBy(lines, by);
        return string.Join("\n", lines);
    }

    public static string SignedInText(
        string? name, string email, string role, string company, string via, string? ip) =>
        string.Join("\n",
            $"🔑 Sign-in: {Who(name, email)}",
            $"Company: {Clean(company)} · {Clean(role)}",
            $"Via: {via}{Ip(ip)}");

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

    public static string PayrollApprovedText(
        string company, string runLabel, int people, decimal totalNet, bool firstEver, string? by)
    {
        var lines = new List<string>
        {
            firstEver
                ? $"🎉 First payroll approved: {Clean(company)}"
                : $"💰 Payroll approved: {Clean(company)}",
            $"Run: {Clean(runLabel)} · {Count(people, "person", "people")} · net {Money("MYR", totalNet)}",
        };
        AddBy(lines, by);
        return string.Join("\n", lines);
    }

    public static string LeaveDecidedText(
        string company, string? name, string email, string leaveType, double days,
        DateTime start, DateTime end, bool approved, bool byAdmin, string? by)
    {
        var head = !approved ? "🚫 Leave rejected" : byAdmin ? "🌴 Leave applied by admin" : "🌴 Leave approved";
        var lines = new List<string>
        {
            $"{head}: {Who(name, email)}",
            $"Company: {Clean(company)}",
            $"{Clean(leaveType)} · {days.ToString("0.#", CultureInfo.InvariantCulture)} day{(days == 1 ? "" : "s")} · {Dates(start, end)}",
        };
        AddBy(lines, by);
        return string.Join("\n", lines);
    }

    public static string ClaimDecidedText(
        string company, string? name, string email, string title, string currency, decimal amount,
        bool approved, string? by)
    {
        var lines = new List<string>
        {
            $"{(approved ? "🧾 Claim approved" : "🚫 Claim rejected")}: {Who(name, email)}",
            $"Company: {Clean(company)}",
            $"{Clean(title)} · {Money(currency, amount)}",
        };
        AddBy(lines, by);
        return string.Join("\n", lines);
    }

    public static string AttendanceRejectedText(
        string company, string? name, string email, string what, DateTime eventAtUtc, string? reason, string? by)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(eventAtUtc, DateTimeKind.Utc), Myt);
        var lines = new List<string>
        {
            $"🚫 Attendance rejected: {Who(name, email)}",
            $"Company: {Clean(company)}",
            $"{Clean(what)} at {local.ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture)}",
        };
        if (!string.IsNullOrWhiteSpace(reason)) lines.Add($"Reason: {Clean(reason)}");
        AddBy(lines, by);
        return string.Join("\n", lines);
    }

    public static string AccountLockedText(
        string? name, string email, bool knownAccount, string? company, TimeSpan duration, string? ip)
    {
        var lines = new List<string>
        {
            $"🔒 Account locked for {Span(duration)}: {Who(name, email)}",
            $"{Modules.Auth.LoginGuard.AccountThreshold} wrong passwords in a row{Ip(ip)}",
        };
        lines.Add(knownAccount
            ? company is null ? "No company" : $"Company: {Clean(company)}"
            : "No AltomateHR account has this email — someone is guessing addresses.");
        return string.Join("\n", lines);
    }

    public static string IpBlockedText(string ip, TimeSpan duration, bool againToday, IReadOnlyList<string> emailsTried)
    {
        const int shown = 5;
        var lines = new List<string>
        {
            $"⛔ IP {Clean(ip)} blocked from signing in for {Span(duration)}"
            + (againToday ? " (blocked again today)" : ""),
            $"{Modules.Auth.LoginGuard.IpThreshold} wrong passwords in {Modules.Auth.LoginGuard.Window.TotalMinutes:0} minutes",
        };
        if (emailsTried.Count > 0)
        {
            var listed = string.Join(", ", emailsTried.Take(shown).Select(Clean));
            lines.Add($"Tried: {listed}{(emailsTried.Count > shown ? $" and {emailsTried.Count - shown} more" : "")}");
        }
        return string.Join("\n", lines);
    }

    private static string Span(TimeSpan d) =>
        d.TotalMinutes >= 60 && d.TotalMinutes % 60 == 0
            ? d.TotalHours == 1 ? "1 hour" : $"{d.TotalHours:0} hours"
            : $"{d.TotalMinutes:0} minutes";

    public static string PortalPasswordViewedText(string company, string portal, string? by, string? ip)
    {
        var lines = new List<string>
        {
            $"👁️ {Clean(portal)} portal password viewed",
            $"Company: {Clean(company)}",
        };
        if (!string.IsNullOrWhiteSpace(by)) lines.Add($"By: {Clean(by)}{Ip(ip)}");
        return string.Join("\n", lines);
    }

    public static string XeroReconnectText(string company, string? xeroOrganisation, string reason)
    {
        var lines = new List<string> { $"🔌 Xero needs reconnecting: {Clean(company)}" };
        if (!string.IsNullOrWhiteSpace(xeroOrganisation)) lines.Add($"Xero organisation: {Clean(xeroOrganisation)}");
        lines.Add($"Why: {Clean(reason)}");
        lines.Add("Nothing syncs to Xero until an admin connects it again.");
        return string.Join("\n", lines);
    }

    public static string PayslipEmailsFailedText(
        string company, string runLabel, int total, IReadOnlyList<string> reasons, string? by)
    {
        var lines = new List<string>
        {
            $"📧 Payslip emails failed: {Clean(company)}",
            $"Run: {Clean(runLabel)} · {reasons.Count} of {Count(total, "payslip", "payslips")} not sent",
        };
        foreach (var group in reasons.GroupBy(r => r).OrderByDescending(g => g.Count()))
            lines.Add($"• {group.Count()} × {Clean(group.Key)}");
        AddBy(lines, by);
        return string.Join("\n", lines);
    }

    public static string PayrollDeletedText(string company, string runLabel, string? by)
    {
        var lines = new List<string>
        {
            $"🗑️ Payroll run deleted: {Clean(company)}",
            $"Run: {Clean(runLabel)} (draft)",
        };
        AddBy(lines, by);
        return string.Join("\n", lines);
    }

    public static string PayrollRevertedText(
        string company, string runLabel, IReadOnlyList<string> alsoReverted, string? by)
    {
        var lines = new List<string>
        {
            $"↩️ Approved payroll reverted to draft: {Clean(company)}",
            $"Run: {Clean(runLabel)}",
        };
        if (alsoReverted.Count > 0)
            lines.Add($"Also back to draft: {string.Join(", ", alsoReverted.Select(Clean))}");
        AddBy(lines, by);
        return string.Join("\n", lines);
    }

    // ---- Helpers ----

    private static void AddBy(List<string> lines, string? by)
    {
        if (!string.IsNullOrWhiteSpace(by)) lines.Add($"By: {Clean(by)}");
    }

    private static string Ip(string? ip) => string.IsNullOrWhiteSpace(ip) ? "" : $" · IP {ip}";

    private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";

    private static string Dates(DateTime start, DateTime end) =>
        start.Date == end.Date
            ? start.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
            : start.Year == end.Year
                ? $"{start.ToString(start.Month == end.Month ? "%d" : "d MMM", CultureInfo.InvariantCulture)}–{end.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}"
                : $"{start.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}–{end.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}";

    public static string Money(string currency, decimal amount)
    {
        var prefix = string.Equals(currency, "MYR", StringComparison.OrdinalIgnoreCase) ? "RM" : Clean(currency);
        return $"{prefix} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
    }

    private static string Who(string? name, string email) =>
        string.IsNullOrWhiteSpace(name) || string.Equals(name, email, StringComparison.OrdinalIgnoreCase)
            ? Clean(email)
            : $"{Clean(name)} ({Clean(email)})";

    // Names and company names are typed by customers. Bitrix reads [B], [URL=…]
    // and the like as formatting, so brackets are softened to plain ones.
    public static string Clean(string text) =>
        text.Replace('[', '(').Replace(']', ')').ReplaceLineEndings(" ").Trim();

    private async Task<string> CompanyNameAsync(string organizationId) =>
        (await _organizations.GetByIdAsync(organizationId))?.Name ?? organizationId;

    // Builds the message only when the channel posts somewhere, and never
    // lets a failure reach the caller.
    private async Task SayAsync(TeamChatChannel channel, string what, Func<Task<string>> build)
    {
        if (!_options.IsConfigured(channel)) return;
        try
        {
            var text = await build();
            if (!_queue.TryEnqueue(new TeamChatMessage(channel, text)))
                _logger.LogWarning("Team chat queue is full; a {What} alert was dropped.", what);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Couldn't queue the {What} team chat alert.", what);
        }
    }
}
