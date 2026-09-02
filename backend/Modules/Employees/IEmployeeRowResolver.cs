namespace AltomateHR.Api.Modules.Employees;

// Resolves "who is this row about?" for the importers, and labels rows for the
// exporters.
//
// A thin projection OVER IDirectoryService, not a rival to it. The shared kernel
// answers "who is this user" and "what is their membership"; this turns one
// tenant-filtered snapshot of that into the two lookups a spreadsheet needs —
// email/name to id, and back again for labelling — including the ambiguous-name
// rule, which is import policy and has no business in the shared kernel.
//
// Deliberately NOT part of IEmployeeService either: that service depends on
// ILeaveService (join-date changes recompute accrual), so a claims/leave/
// attendance service injecting it would close a DI cycle.
public interface IEmployeeRowResolver
{
    // One tenant-filtered snapshot of the current org's members, built once per
    // import rather than one lookup per row.
    Task<EmployeeRowIndex> GetSnapshotAsync();
}

// Email/name to user id, plus the reverse for export labelling.
public sealed class EmployeeRowIndex
{
    private readonly Dictionary<string, string> _idByEmail;
    private readonly Dictionary<string, List<string>> _idsByName;
    private readonly Dictionary<string, EmployeeIdentity> _byId;

    internal EmployeeRowIndex(IEnumerable<EmployeeIdentity> members)
    {
        var list = members.ToList();

        _byId = list.ToDictionary(m => m.Id, StringComparer.Ordinal);

        _idByEmail = list
            .GroupBy(m => m.Email.Trim().ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

        // Plural on purpose: names are NOT unique. A collision has to be an
        // error the admin resolves with an email, not a coin flip that files
        // someone else's leave.
        _idsByName = list
            .Where(m => NormalizeName(m.Name).Length > 0)
            .GroupBy(m => NormalizeName(m.Name), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Id).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);
    }

    public IReadOnlyCollection<EmployeeIdentity> Members => _byId.Values;

    public EmployeeIdentity? ById(string? id) =>
        id is not null && _byId.TryGetValue(id, out var found) ? found : null;

    public string EmailOf(string? id) => ById(id)?.Email ?? string.Empty;

    public string NameOf(string? id) => ById(id)?.Name ?? string.Empty;

    // Email first (unambiguous), then name. Ambiguous=true means the name
    // matched more than one person and the caller must reject the row.
    public (string? Id, bool Ambiguous) Resolve(string? email, string? name)
    {
        if (!string.IsNullOrWhiteSpace(email) &&
            _idByEmail.TryGetValue(email.Trim().ToLowerInvariant(), out var byEmail))
            return (byEmail, false);

        if (!string.IsNullOrWhiteSpace(name) &&
            _idsByName.TryGetValue(NormalizeName(name), out var byName))
            return byName.Count == 1 ? (byName[0], false) : (null, true);

        return (null, false);
    }

    // Strips accents and collapses punctuation/spacing, so a Jibble export's
    // "AHMAD  BIN ALI" lines up with our "Ahmad bin Ali".
    private static string NormalizeName(string name)
    {
        var decomposed = name.Normalize(System.Text.NormalizationForm.FormD);
        var letters = decomposed
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');

        return string.Join(' ', new string(letters.ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

public sealed record EmployeeIdentity(string Id, string Email, string Name, string Role);

// The identity columns every importer shares, and the fact that they SUBSTITUTE
// for one another: email is preferred (unambiguous), but a file exported from
// another system often carries only names.
//
// Defined here, next to the resolver, so the three importers can't disagree
// about which column keys mean "who is this row about".
public static class EmployeeImportColumns
{
    public const string EmailKey = "employeeEmail";
    public const string NameKey = "employeeName";

    // Pass to TabularHeaderMap.Build: at least one of the two must be present.
    public static readonly IReadOnlyList<IReadOnlyList<string>> IdentityGroup =
        [[EmailKey, NameKey]];
}
