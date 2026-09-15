namespace AltomateHR.Api.Modules.Attendance;

// Resolves the admin attendance filters (project / team / search) to a set of
// employee ids. Extracted so every admin-facing attendance report narrows
// through exactly one definition of what a filter means — the reports are read
// side by side, and two implementations would eventually disagree about who is
// "on Website Revamp".
public interface IAttendanceScopeService
{
    // The employee ids a filter narrows to, or null for "everyone".
    //
    // Null and empty mean different things and callers must not conflate them:
    // null is no filter at all, empty is a filter that matched nobody — which
    // has to return no rows rather than the whole org.
    Task<HashSet<string>?> ResolveAsync(string? projectId, string? teamId, string? q);
}
