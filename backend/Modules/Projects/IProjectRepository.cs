using AltomateHR.Api.Modules.Projects.Entities;

namespace AltomateHR.Api.Modules.Projects;

public interface IProjectRepository
{
    Task<List<Project>> GetAllAsync();
    Task<Project?> GetByIdAsync(string id);
    Task<Project> AddAsync(Project project);
    Task UpdateAsync(Project project);

    // A project's geofenced sites, in the order the check must walk them.
    Task<List<ProjectGeofencePoint>> GetGeofencePointsAsync(string projectId);

    // A project's IP allowlist entries.
    Task<List<ProjectAllowedIp>> GetAllowedIpsAsync(string projectId);

    // Replace-all. One transaction each, so a save cannot leave a project half
    // geofenced or half allowlisted.
    Task ReplaceGeofencePointsAsync(string projectId, IReadOnlyList<ProjectGeofencePoint> points);
    Task ReplaceAllowedIpsAsync(string projectId, IReadOnlyList<ProjectAllowedIp> entries);

    // How many sites / allowlist entries each project has, for the settings
    // grid. One query each rather than loading the rows per project.
    Task<Dictionary<string, int>> GetGeofencePointCountsAsync();
    Task<Dictionary<string, int>> GetAllowedIpCountsAsync();
}
