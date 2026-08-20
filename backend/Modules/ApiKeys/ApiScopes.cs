namespace AltomateHR.Api.Modules.ApiKeys;

// The fixed set of permissions a wp_live_ key can be granted. A key's Scopes column
// holds a comma-separated subset of these. Mirrors the monolith's curated catalog.
// A ":read" scope covers list/detail GETs; ":write" covers create/update/approve.
public static class ApiScopes
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "employees:read",  "employees:write",
        "claims:read",     "claims:write",
        "leave:read",      "leave:write",
        "attendance:read", "attendance:write",
        "projects:read",   "projects:write",
        "teams:read",      "teams:write",
        "settings:read",   "settings:write",
        "approvals:write",
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
