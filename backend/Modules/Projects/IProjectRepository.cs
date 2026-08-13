using AltomateHR.Api.Modules.Projects.Entities;

namespace AltomateHR.Api.Modules.Projects;

public interface IProjectRepository
{
    Task<List<Project>> GetAllAsync();
    Task<Project?> GetByIdAsync(string id);
    Task<Project> AddAsync(Project project);
    Task UpdateAsync(Project project);
}
