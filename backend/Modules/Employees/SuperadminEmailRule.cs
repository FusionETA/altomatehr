using AltomateHR.Api.Common;
using AltomateHR.Api.Modules.Auth;

namespace AltomateHR.Api.Modules.Employees;

// Superadmin is granted by EMAIL (SUPERADMIN_EMAILS), checked live on every
// request. So whoever can put one of those addresses on an account they hold
// the password to is a superadmin, with every company open to them. A customer
// admin must never be able to: not by creating a login with that address, and
// not by renaming one to it. Altomate support (already a superadmin) may.
public static class SuperadminEmailRule
{
    public const string Refusal = "That email address is reserved for Altomate staff.";

    // Null when the address may be given to a login; otherwise the refusal.
    public static string? RefusalFor(ISuperadminRegistry? superadmins, ICurrentUser? caller, string email) =>
        !(caller?.IsSupport ?? false) && (superadmins?.IsSuperadmin(email) ?? false) ? Refusal : null;
}
