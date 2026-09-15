using AltomateHR.Api.Modules.Employees;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Attendance;

public class AttendanceScopeService : IAttendanceScopeService
{
    private readonly IEmployeeRowResolver _employees;
    private readonly ITeamService _teams;

    public AttendanceScopeService(IEmployeeRowResolver employees, ITeamService teams)
    {
        _employees = employees;
        _teams = teams;
    }

    public async Task<HashSet<string>?> ResolveAsync(string? projectId, string? teamId, string? q)
    {
        HashSet<string>? scope = null;

        if (!string.IsNullOrWhiteSpace(teamId))
        {
            scope = (await _teams.GetMemberEmployeeIdsAsync(teamId)).ToHashSet(StringComparer.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(projectId))
        {
            // Employees reach a project through their teams, so "on this
            // project" is the union of the rosters of every team on it.
            var onProject = (await _teams.GetAllAsync())
                .Where(t => t.ProjectId == projectId)
                .SelectMany(t => t.Members.Select(m => m.EmployeeId))
                .ToHashSet(StringComparer.Ordinal);

            scope = scope is null ? onProject : scope.Intersect(onProject).ToHashSet(StringComparer.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var directory = await _employees.GetSnapshotAsync();
            var matched = directory.Members
                .Where(m =>
                    (m.Name ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    (m.Email ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(m => m.Id)
                .ToHashSet(StringComparer.Ordinal);

            scope = scope is null ? matched : scope.Intersect(matched).ToHashSet(StringComparer.Ordinal);
        }

        return scope;
    }
}
