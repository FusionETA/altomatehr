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
}
