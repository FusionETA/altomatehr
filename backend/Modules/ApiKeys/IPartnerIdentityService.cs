namespace AltomateHR.Api.Modules.ApiKeys;

// Who an integration is talking about, for the identity endpoints
// (POST /auth/verify, /auth/organizations): "is this email + password an
// admin, and which companies do they run?"
//
// Two kinds of caller:
//   • an org key (wp_live_) — answers only about ITS company;
//   • a MASTER key (wp_master_) — a first-party companion app (ABPay) whose
//     owner runs several companies. It can confirm a supplied password and
//     list the companies that person administers, and nothing more: it is not
//     an auth scheme, so no data endpoint accepts it.
public interface IPartnerIdentityService
{
    // Null → neither a valid master key nor an org key.
    Task<PartnerCaller?> ResolveCallerAsync(string? bearerToken, string? keyOrganizationId);

    // Null → wrong password, unknown email, or not an admin of what the
    // caller may ask about (one answer, so nobody learns which emails exist).
    Task<PartnerIdentity?> VerifyAsync(PartnerCaller caller, string email, string password);

    // The companies a person administers, by user id or email. Empty when
    // they administer none the caller may ask about.
    Task<IReadOnlyList<PartnerOrganization>> AdminOrganizationsAsync(
        PartnerCaller caller, string? userId, string? email);

    // What ONE person may reach in ONE company, for a companion app that holds
    // the company's key (which sees everything) and must narrow itself to the
    // signed-in admin's Manage access. Master key only.
    //
    // Null → no such user, not an admin or owner of that company, or the
    // company is deactivated. One answer for all three, like VerifyAsync.
    Task<PartnerAccess?> AccessAsync(PartnerCaller caller, string userId, string organizationId);

    Task<string?> KeyNameAsync(string apiKeyId);
}

// `Employees` is null when `AllEmployees` — nobody wants a list of thousands
// of ids that only says "no restriction".
public sealed record PartnerAccess(
    string UserId,
    string OrganizationId,
    string Role,
    bool AllEmployees,
    bool CanRunPayroll,
    bool CanChangeSettings,
    IReadOnlyDictionary<string, string> Modules,
    IReadOnlyList<PartnerAccessEmployee>? Employees);

// `UserId` is what GET /employees returns as `id`, and what GET
// /payroll/employees returns as `userId` — the id to MATCH people by, on both.
//
// `EmployeeProfileId` is their payroll identity (payslips, run members), null
// for someone whose payroll details were never saved. Do not filter the
// payroll roster by it: that endpoint stands a BLANK profile in for such a
// person, so the `employeeProfileId` it shows them under is a throwaway Guid
// that differs on every call (`hasPayrollProfile: false` is how it says so).
// Filtering on it would hide exactly the people the roster exists to flag as
// not ready to file.
public sealed record PartnerAccessEmployee(string UserId, string? EmployeeProfileId);

public sealed record PartnerCaller(bool IsMaster, string? OrganizationId);

public sealed record PartnerIdentity(
    string Id,
    string Name,
    string Email,
    string Role,
    string OrganizationId,
    string OrganizationName,
    IReadOnlyList<PartnerOrganization> Organizations);

// `OrganizationId` duplicates `Id` for callers written against the earlier
// shape of /auth/organizations ({ organizationId, role }).
//
// `AllEmployees` and `CanRunPayroll` are this person's Manage access in that
// company, so a companion knows at sign-in which companies it must narrow
// (POST /auth/access) and in which it must hide the payroll actions. Both are
// always true for an Owner.
//
// NULL for a company (wp_live_) key: "we are not telling you". These endpoints
// take a bare email with no password, so answering them for an org key would
// let a company key walk its own address list and build a directory of which
// of its admins are restricted — the very thing /auth/access 403s it for.
// Only the platform's master key is told.
public sealed record PartnerOrganization(
    string Id, string Name, string Role, bool? AllEmployees = null, bool? CanRunPayroll = null)
{
    public string OrganizationId => Id;
}
