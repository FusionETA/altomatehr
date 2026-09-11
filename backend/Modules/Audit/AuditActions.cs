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

    // Payroll configuration — what these decide ends up on a statutory filing,
    // so every change is recorded.
    public const string PayrollSettingsUpdate = "payroll.settings.update";
    public const string PayrollCompanyInfoUpdate = "payroll.company-info.update";
    public const string PayrollRunXeroSync = "payroll.run.xero-sync";

    // Staff loans. What an employee repays each month comes off their pay, so
    // who recorded the loan and on what terms is an audit question.
    public const string PayrollLoanCreate = "payroll.loan.create";
    public const string PayrollLoanUpdate = "payroll.loan.update";
    public const string PayrollLoanDelete = "payroll.loan.delete";

    // A salary change is read back by LHDN, by an IR dispute, and by a
    // retrenchment payout turning on "last drawn salary".
    public const string PayrollSalaryChange = "payroll.salary-change";

    // Saved statutory-portal logins. Reading one discloses a password, so
    // the READ is audited here as well as the writes — that is the whole
    // reason this vocabulary has a "reveal".
    public const string PayrollPortalCredentialSave = "payroll.portal-credential.save";
    public const string PayrollPortalCredentialReveal = "payroll.portal-credential.reveal";
    public const string PayrollPortalCredentialDelete = "payroll.portal-credential.delete";

    // Seeding a year of history writes SUBMITTED payslips that every later
    // month's PCB reads from. Worth recording who did it and when.
    public const string PayrollYtdImport = "payroll.ytd-import";
    public const string PayrollEmployeeImport = "payroll.employee-import";

    // Payroll runs. Generation is destructive — it replaces the run's
    // payslips — so the log is the only record that an earlier set existed.
    public const string PayrollRunCreate = "payroll.run.create";
    public const string PayrollRunGenerate = "payroll.run.generate";

    // Per-run inputs that SURVIVE a generation — the hand-entered overtime,
    // one-off pay and attached reimbursements. Generation rebuilds payslips
    // from these, so a figure nobody can explain is traced back through here
    // rather than through the payslip it landed on.
    public const string PayrollRunAdjustmentSave = "payroll.run.adjustment.save";
    public const string PayrollRunAdjustmentClear = "payroll.run.adjustment.clear";
    public const string PayrollRunClaimAttach = "payroll.run.claim.attach";
    public const string PayrollRunClaimDetach = "payroll.run.claim.detach";

    // The status machine. SUBMITTED is what every later run's year-to-date
    // reads from, so who moved a run in or out of it — and when — is the
    // question an audit of a filing actually asks.
    public const string PayrollRunSubmitForApproval = "payroll.run.submit-for-approval";
    public const string PayrollRunApprove = "payroll.run.approve";
    public const string PayrollRunRejectApproval = "payroll.run.reject-approval";
    public const string PayrollRunRevertToDraft = "payroll.run.revert-to-draft";
    public const string PayrollRunDelete = "payroll.run.delete";

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
