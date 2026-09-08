using System.Security.Claims;

namespace AltomateHR.Api.Common;

// The administrative roles, and the rule that they are NOT part of any
// approval hierarchy.
//
// Administering the org is a separate concern from being in the chain of
// command. An Admin/Owner is an oversight seat: they can SEE everything in the
// org — someone's breaks, hours, leave balances — and they approve nothing.
// They are never an approver in a team chain, never the supervisor fallback,
// and never occupy a layer. The people in the hierarchy are Employees and
// Supervisors.
//
// The consequence is deliberate: strip the admin out of a team and the person
// at the top has no one above them, so their own submissions have nobody to
// route to. That case is handled at submit (see IApprovalRouter.StepCountAsync
// returning 0) rather than by quietly handing the admin approval power back.
public static class OrgRoles
{
    public const string Admin = "Admin";
    public const string Owner = "Owner";

    private static readonly string[] Administrative = [Admin, Owner];

    public static bool IsAdministrative(string? role) =>
        role is not null && Administrative.Contains(role, StringComparer.OrdinalIgnoreCase);

    // The same question asked of a signed-in caller.
    //
    // Controllers reached for User.IsInRole("Admin") for this, which silently
    // excludes the Owner — the one seat guaranteed to exist in every org. That
    // read as missing data rather than as a permission failure: an owner opening
    // the org roll call was handed their own records and no error.
    public static bool IsAdministrative(this ClaimsPrincipal user) =>
        Administrative.Any(user.IsInRole);
}
