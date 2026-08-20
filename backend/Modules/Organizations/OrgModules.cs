namespace AltomateHR.Api.Modules.Organizations;

// Single source of truth for "which modules does this org / this admin have".
// Two inputs combine:
//   1. The ORG package (plan + tier + addons) → the modules the org is entitled to (a ceiling).
//   2. A per-admin grant (OrganizationMembership.Modules) → narrows below the ceiling.
// Effective access = ceiling ∩ grant. Ports the monolith's deriveOrgEnabledModules.
public static class OrgModules
{
    // Module keys. Also the valid entries in an admin's module grant.
    public const string Employees = "employees";
    public const string Leave = "leave";
    public const string Projects = "projects";
    public const string Teams = "teams";
    public const string Accounts = "accounts";
    public const string Policies = "policies";
    public const string Overtime = "overtime";
    public const string Claims = "claims";        // addon: expense_claim
    public const string Attendance = "attendance"; // addon: clock

    // Everyone gets these regardless of plan/tier/addons — core HR + admin tools.
    private static readonly string[] BaseModules =
        { Employees, Leave, Projects, Teams, Accounts, Policies, Overtime };

    // Addon key → the module(s) it unlocks. Claims + Attendance are the only paid ones.
    private static readonly Dictionary<string, string[]> AddonToModules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["expense_claim"] = new[] { Claims },
        ["clock"] = new[] { Attendance },
    };

    public static readonly IReadOnlyList<string> AllModules =
        BaseModules.Concat(new[] { Claims, Attendance }).ToList();

    public static readonly IReadOnlyList<string> AllAddons = AddonToModules.Keys.ToList();

    public static bool IsKnownModule(string m) => AllModules.Contains(m, StringComparer.OrdinalIgnoreCase);
    public static bool IsKnownAddon(string a) => AddonToModules.ContainsKey(a);

    // The org's entitlement ceiling.
    //   DIY + FREE  → base only (addons IGNORED — free never unlocks paid modules).
    //   DIY + PAID  → base + every addon's modules.
    //   EXPERT      → base + every addon's modules (same surface as DIY Paid).
    public static IReadOnlyCollection<string> DeriveOrgEnabledModules(
        OrgPlan plan, OrgPlanTier? tier, IEnumerable<string> addons)
    {
        var set = new HashSet<string>(BaseModules, StringComparer.OrdinalIgnoreCase);

        if (plan == OrgPlan.DIY && tier == OrgPlanTier.FREE)
            return set;

        foreach (var addon in addons)
            if (AddonToModules.TryGetValue(addon.Trim(), out var mods))
                foreach (var m in mods) set.Add(m);

        return set;
    }

    // Effective access = org ceiling ∩ admin grant. A null grant means "no restriction"
    // (owners, legacy members, and wp_live keys) → the full ceiling.
    public static IReadOnlyCollection<string> Effective(
        IReadOnlyCollection<string> orgEnabled, IReadOnlyCollection<string>? adminGrant)
    {
        if (adminGrant is null) return orgEnabled;
        return orgEnabled
            .Where(m => adminGrant.Contains(m, StringComparer.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    // csv column <-> list. Blank → empty (never [""]).
    public static IReadOnlyList<string> Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Join(IEnumerable<string> values) => string.Join(",", values);
}
