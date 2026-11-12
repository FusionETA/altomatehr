using AltomateHR.Api.Modules.TeamChat;

namespace AltomateHR.Api.Tests.TeamChat;

// Stands in for the team's Bitrix chats: each call is written down as one
// line, so a test can say which alert went out and with what.
public sealed class RecordingTeamChat : ITeamChatAlerts
{
    public List<string> Said { get; } = [];

    private Task Say(string line)
    {
        Said.Add(line);
        return Task.CompletedTask;
    }

    public Task CompanyCreatedAsync(string organizationId, string? ownerUserId, string via) =>
        Say($"company-created {organizationId} {via}");

    public Task CompanyStatusChangedAsync(string organizationId, bool deactivated, string? reason) =>
        Say($"company-status {organizationId} {deactivated}");

    public Task PayrollRunApprovedAsync(string organizationId, string runLabel, int people, decimal totalNet, bool firstEver) =>
        Say($"payroll-approved {runLabel} first={firstEver}");

    public Task LeaveDecidedAsync(string organizationId, string employeeUserId, string leaveType, double days,
        DateTime start, DateTime end, bool approved, bool byAdmin) =>
        Say($"leave {employeeUserId} approved={approved} byAdmin={byAdmin}");

    public Task ClaimDecidedAsync(string organizationId, string employeeUserId, string title, string currency,
        decimal amount, bool approved) =>
        Say($"claim {employeeUserId} {title} approved={approved}");

    public Task AttendanceRejectedAsync(string organizationId, string employeeUserId, string what,
        DateTime eventAtUtc, string? reason) =>
        Say($"attendance-rejected {employeeUserId} {what}");

    public Task SignedInAsync(string userId, string role, string organizationId, string via) =>
        Say($"sign-in {userId} {via}");

    public Task AccountLockedAsync(string email, string? userId, string? organizationId, TimeSpan duration, string? ip) =>
        Say($"account-locked {email}");

    public Task IpBlockedAsync(string ip, TimeSpan duration, bool againToday, IReadOnlyList<string> emailsTried) =>
        Say($"ip-blocked {ip}");

    public Task PortalPasswordViewedAsync(string organizationId, string portal) =>
        Say($"portal-password {portal}");

    public Task XeroReconnectRequiredAsync(string organizationId, string? xeroOrganisation, string reason) =>
        Say($"xero-reconnect {organizationId}");

    public Task PayslipEmailsFailedAsync(string organizationId, string runLabel, int total, IReadOnlyList<string> reasons) =>
        Say($"payslip-emails {runLabel} {reasons.Count}/{total}");

    public Task PayrollRunDeletedAsync(string organizationId, string runLabel) =>
        Say($"payroll-deleted {runLabel}");

    public Task PayrollRunRevertedAsync(string organizationId, string runLabel, IReadOnlyList<string> alsoReverted) =>
        Say($"payroll-reverted {runLabel} also={alsoReverted.Count}");
}
