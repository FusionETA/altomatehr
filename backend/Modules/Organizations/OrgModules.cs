namespace AltomateHR.Api.Modules.Organizations;

// Single source of truth for "which modules does this org / this admin have".
// Two inputs combine:
//   1. The ORG package (plan + tier + addons) → the modules the org is entitled to (a ceiling).
//   2. A per-admin grant (OrganizationMembership.Modules) → narrows below the ceiling.
// Effective access = ceiling ∩ grant. Ports the monolith's deriveOrgEnabledModules.
public enum ModuleLevel { None = 0, View = 1, Manage = 2 }

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
    // Both were missing, so a grant could never withhold them: every Admin saw
    // payroll (salaries, ICs, bank accounts) and the activity log whatever the
    // Owner ticked. The previous system had both as grantable base modules.
    public const string Payroll = "payroll";
    public const string Audit = "audit";
    // HR letters from templates (offer, confirmation, warning …). Admin-only.
    public const string Documents = "documents";

    // Everyone gets these regardless of plan/tier/addons — core HR + admin tools.
    private static readonly string[] BaseModules =
        { Employees, Leave, Projects, Teams, Accounts, Policies, Overtime, Payroll, Audit, Documents };

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

    // ─── Per-module level ─────────────────────────────────────────────
    //
    // An admin's grant (OrganizationMembership.Modules) is a csv of entries:
    //   "payroll"       → Manage: see it and act (edit, approve, run).
    //   "payroll:view"  → View: see it and download; every change is refused.
    // A module not listed is Off. Plain keys predate levels and keep meaning
    // Manage, so every existing grant reads exactly as it did.
    public const string ViewSuffix = ":view";

    public static IReadOnlyDictionary<string, ModuleLevel> ParseGrant(string? csv)
    {
        var grant = new Dictionary<string, ModuleLevel>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Split(csv))
        {
            var isView = entry.EndsWith(ViewSuffix, StringComparison.OrdinalIgnoreCase);
            var key = isView ? entry[..^ViewSuffix.Length] : entry;
            if (!IsKnownModule(key)) continue;
            // Manage wins if a key somehow appears at both levels.
            if (!grant.TryGetValue(key, out var existing) || existing < (isView ? ModuleLevel.View : ModuleLevel.Manage))
                grant[key] = isView ? ModuleLevel.View : ModuleLevel.Manage;
        }
        return grant;
    }

    public static string FormatGrant(IEnumerable<KeyValuePair<string, ModuleLevel>> grant) =>
        Join(grant
            .Where(g => g.Value != ModuleLevel.None)
            .Select(g => g.Value == ModuleLevel.View ? g.Key + ViewSuffix : g.Key));

    // The module keys a grant opens at any level — what the nav and the
    // plan-ceiling intersection work with.
    public static IReadOnlyList<string> GrantedKeys(string? csv) => ParseGrant(csv).Keys.ToList();

    // csv column <-> list. Blank → empty (never [""]).
    public static IReadOnlyList<string> Split(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Join(IEnumerable<string> values) => string.Join(",", values);
}
