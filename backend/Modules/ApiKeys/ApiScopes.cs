namespace AltomateHR.Api.Modules.ApiKeys;

// The fixed set of permissions a wp_live_ key can be granted. A key's Scopes column
// holds a comma-separated subset of these. One pair per data domain: ":read" gates the
// GET (list/detail) endpoints; ":write" gates create/update/approve (wired later).
public static class ApiScopes
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "employees:read",     "employees:write",
        "claims:read",        "claims:write",
        "leave:read",         "leave:write",
        "attendance:read",    "attendance:write",
        "overtime:read",      "overtime:write",
        "projects:read",      "projects:write",
        "teams:read",         "teams:write",
        "accounts:read",      "accounts:write",
        "policies:read",      "policies:write",
        "organizations:read", "organizations:write",
        "notifications:write",   // no :read pair — there's nothing to list, only send
        // Mint an SSO hand-off ticket for one of this org's admins. Write-only
        // for the same reason: there is nothing to read, only a door to open.
        "sso:write",
        // Payroll. ":write" covers submitting, approving, rejecting and reverting
        // a run, its adjustments, and payroll settings — the same meaning as the
        // previous system's scope. A run is money leaving a company, so a key
        // only gets it when it was explicitly granted.
        "payroll:read",       "payroll:write",
    };

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    public static bool IsKnown(string scope) => Known.Contains(scope);

    // Column <-> list conversions. Empty/blank column → no scopes (never [""]).
    public static IReadOnlyList<string> Split(string csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? Array.Empty<string>()
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Join(IEnumerable<string> scopes) => string.Join(",", scopes);
}
