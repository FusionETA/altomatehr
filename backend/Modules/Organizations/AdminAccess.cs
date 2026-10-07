namespace AltomateHR.Api.Modules.Organizations;

// What the signed-in person may do as an ADMIN in the active org, from the
// Owner's "Manage access" choices (OrganizationMembership.Modules /
// PolicyScope / CanChangeSettings). Three independent limits:
//
//   · Modules  — per module Off / View / Manage.
//   · Employees — every employee, or only those on chosen policies.
//   · Settings — may or may not change the company's configuration.
//
// Only an Admin is ever limited. An Owner has everything; an Employee or
// Supervisor has no admin surface for these to narrow; an API key is gated by
// its scopes instead.
public sealed record AdminAccess(
    bool Restricted,
    IReadOnlyDictionary<string, ModuleLevel>? Grant,
    bool CanChangeSettings,
    IReadOnlyList<string>? PolicyScope)
{
    public static readonly AdminAccess Full = new(false, null, true, null);

    // Grant null = every module the plan allows, at Manage.
    public ModuleLevel LevelFor(string module) =>
        Grant is null ? ModuleLevel.Manage : Grant.GetValueOrDefault(module, ModuleLevel.None);

    public bool HasFullEmployeeScope => PolicyScope is null;
}
