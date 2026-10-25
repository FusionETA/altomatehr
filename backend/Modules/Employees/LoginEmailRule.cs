using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Employees;

// A login email is global: it signs that person in to EVERY company they
// belong to. Changing it from one company could hand another company's access
// to whoever owns the new address. So beyond the company the caller is
// working in (where they already manage this person), every company the login
// opens must be one the caller owns. Altomate support may always.
public static class LoginEmailRule
{
    public const string SharedLoginRefusal =
        "This person's login is also used in a company you don't own, so its email can't be "
        + "changed from here. Ask Altomate support to change it.";

    // Null when the change is allowed; otherwise the refusal to show.
    public static async Task<string?> RefusalAsync(
        IOrganizationMembershipRepository memberships, ICurrentUser? caller, string userId)
    {
        if (caller?.IsSupport ?? false) return null;

        var callerId = caller?.UserId;
        if (callerId is null || caller!.OrganizationId is null) return SharedLoginRefusal;

        var owned = (await memberships.GetByUserAsync(callerId))
            .Where(m => string.Equals(m.Role, "Owner", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.OrganizationId)
            .ToHashSet(StringComparer.Ordinal);

        return (await memberships.GetByUserAsync(userId))
            .Any(m => m.OrganizationId != caller!.OrganizationId && !owned.Contains(m.OrganizationId))
            ? SharedLoginRefusal
            : null;
    }
}
