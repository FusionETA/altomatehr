using AltomateHR.Api.Modules.Projects.Dtos;

namespace AltomateHR.Api.Modules.Projects;

public interface IProjectService
{
    Task<IEnumerable<ProjectDto>> GetAllAsync();

    // The projects this user can actually work against: on one of their teams,
    // and not archived.
    Task<IEnumerable<ProjectDto>> GetForMemberAsync(string userId);
    Task<ProjectDto?> GetByIdAsync(string id);
    Task<ProjectDto> CreateAsync(SaveProjectDto dto);
    Task<ProjectDto?> UpdateAsync(string id, SaveProjectDto dto);
    Task<ProjectDto?> SetArchivedAsync(string id, bool archived);
}
