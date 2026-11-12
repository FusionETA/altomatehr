namespace AltomateHR.Api.Modules.TeamChat;

// What the team's Bitrix chats hear about, as it happens. Every call is
// best-effort and never throws: a chat being down must not stop anyone
// signing in, approving pay or creating a company. Which chat each goes to
// is in its comment; TeamChatChannel says what each chat is for.
public interface ITeamChatAlerts
{
    // ---- Action chat: what customers did ----

    // via: how it was made, e.g. "self-serve", "Support" or "provisioning API".
    Task CompanyCreatedAsync(string organizationId, string? ownerUserId, string via);

    // A superadmin deactivated or reactivated it; by whom comes from the session.
    Task CompanyStatusChangedAsync(string organizationId, bool deactivated, string? reason);

    // firstEver: the company's first computed run to be approved.
    Task PayrollRunApprovedAsync(string organizationId, string runLabel, int people, decimal totalNet, bool firstEver);

    // A final decision only (not a step of a longer chain). byAdmin: an admin
    // applied it for the employee, so it was approved without a request.
    Task LeaveDecidedAsync(
        string organizationId, string employeeUserId, string leaveType, double days,
        DateTime start, DateTime end, bool approved, bool byAdmin);

    // A final decision only.
    Task ClaimDecidedAsync(
        string organizationId, string employeeUserId, string title, string currency, decimal amount, bool approved);

    // Approvals of clock-ins and outs are every shift, so only refusals are said.
    Task AttendanceRejectedAsync(
        string organizationId, string employeeUserId, string what, DateTime eventAtUtc, string? reason);

    // ---- Alert chat: what the team may need to look at ----

    // via: how they signed in, e.g. "password" or "Altomate SSO".
    Task SignedInAsync(string userId, string role, string organizationId, string via);

    // LoginGuard locked an account (email as typed; user and company when it
    // is a real account) or blocked an IP — said once per lock or block.
    Task AccountLockedAsync(string email, string? userId, string? organizationId, TimeSpan duration, string? ip);

    // againToday: its second (or later) block today, so the longer one.
    Task IpBlockedAsync(string ip, TimeSpan duration, bool againToday, IReadOnlyList<string> emailsTried);

    // portal: "KWSP", "PERKESO", "LHDN" and so on.
    Task PortalPasswordViewedAsync(string organizationId, string portal);

    Task XeroReconnectRequiredAsync(string organizationId, string? xeroOrganisation, string reason);

    // reasons: why each failed send failed, one per failure.
    Task PayslipEmailsFailedAsync(string organizationId, string runLabel, int total, IReadOnlyList<string> reasons);

    Task PayrollRunDeletedAsync(string organizationId, string runLabel);

    // alsoReverted: later runs the revert sent back to draft with it.
    Task PayrollRunRevertedAsync(string organizationId, string runLabel, IReadOnlyList<string> alsoReverted);
}
