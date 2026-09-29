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

    Task<string?> KeyNameAsync(string apiKeyId);
}

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
public sealed record PartnerOrganization(string Id, string Name, string Role)
{
    public string OrganizationId => Id;
}
