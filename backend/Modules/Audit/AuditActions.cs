namespace AltomateHR.Api.Modules.Audit;

// The action vocabulary, namespaced "module.verb" so the feed can be grouped and
// filtered, and so a code stays greppable in the database long after the UI
// label has been reworded.
//
// Keep it CURATED. An audit log is only readable if it records things an admin
// would actually ask about — who changed the org's configuration — not every
// page view, form keystroke and cache miss.
public static class AuditActions
{
    // Org configuration
    public const string SettingsOrgUpdate = "settings.org.update";
    public const string SettingsClaimsUpdate = "settings.claims.update";

    // Chart of accounts
    public const string AccountCreate = "coa.create";
    public const string AccountUpdate = "coa.update";
    public const string AccountArchive = "coa.archive";
    public const string AccountRestore = "coa.restore";

    // Projects
    public const string ProjectCreate = "project.create";
    public const string ProjectUpdate = "project.update";
    public const string ProjectArchive = "project.archive";
    public const string ProjectRestore = "project.restore";

    // People
    public const string EmployeeCreate = "employee.create";
    public const string EmployeeUpdate = "employee.update";

    // Teams / approval hierarchy
    public const string TeamCreate = "team.create";
    public const string TeamUpdate = "team.update";
    public const string TeamDelete = "team.delete";
    // Membership is the part that actually decides who approves whom, so it is
    // its own event rather than a "team updated" with the detail buried.
    public const string TeamMemberSet = "team.member.set";
    public const string TeamMemberRemove = "team.member.remove";

    // Bulk-resolves requests left with no approver — it can approve real money
    // in one call, which makes it the single most audit-worthy action here.
    public const string ApprovalsReconcile = "approvals.reconcile";

    // Xero
    public const string XeroConnect = "xero.connect";
    public const string XeroDisconnect = "xero.disconnect";
    public const string XeroSyncAccounts = "xero.accounts.sync";

    // Auth. Successful sign-ins are recorded as well as failed ones: "has this
    // supervisor been in this week" is a question the log gets asked, and a feed
    // of only failures cannot answer it.
    public const string AuthLogin = "auth.login";
    public const string AuthLoginFailed = "auth.login.failed";
    public const string AuthPasswordChange = "auth.password.change";

    // Approvals are deliberately NOT audited here. Claims, leave, attendance and
    // overtime each show their own decisions on their own tab, with more context
    // than a one-line audit row could carry — duplicating them into this feed
    // would bury the configuration changes it exists for and give two places to
    // read the same fact from.

    // Short sentences for the UI. The row keeps the code for forensics; this is
    // only what an admin reads.
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        [SettingsOrgUpdate] = "Organisation settings updated",
        [SettingsClaimsUpdate] = "Claim settings updated",
        [AccountCreate] = "Account created",
        [AccountUpdate] = "Account updated",
        [AccountArchive] = "Account archived",
        [AccountRestore] = "Account restored",
        [ProjectCreate] = "Project created",
        [ProjectUpdate] = "Project updated",
        [ProjectArchive] = "Project archived",
        [ProjectRestore] = "Project restored",
        [EmployeeCreate] = "Employee added",
        [EmployeeUpdate] = "Employee updated",
        [TeamCreate] = "Team created",
        [TeamUpdate] = "Team updated",
        [TeamDelete] = "Team deleted",
        [TeamMemberSet] = "Team member added or moved",
        [TeamMemberRemove] = "Team member removed",
        [ApprovalsReconcile] = "Unreachable approvals resolved",
        [XeroConnect] = "Xero connected",
        [XeroDisconnect] = "Xero disconnected",
        [XeroSyncAccounts] = "Xero accounts synced",
        [AuthLogin] = "Signed in",
        [AuthLoginFailed] = "Failed sign-in",
        [AuthPasswordChange] = "Password changed",
    };

    // Exact match first, then a generic prettifier — an action wired up without
    // a label still reads, just less polished, rather than showing a raw code.
    public static string Humanize(string action)
    {
        if (Labels.TryGetValue(action, out var label)) return label;

        var words = action.Replace('.', ' ').Trim();
        return words.Length == 0
            ? action
            : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
