using AltomateHR.Api.Modules.Projects.Dtos;

namespace AltomateHR.Api.Modules.Projects;

public interface IProjectService
{
    Task<IEnumerable<ProjectDto>> GetAllAsync();
    Task<ProjectDto?> GetByIdAsync(string id);
    Task<ProjectDto> CreateAsync(SaveProjectDto dto);
    Task<ProjectDto?> UpdateAsync(string id, SaveProjectDto dto);
    Task<ProjectDto?> SetArchivedAsync(string id, bool archived);
}
