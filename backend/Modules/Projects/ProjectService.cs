using AltomateHR.Api.Modules.Projects.Dtos;
using AltomateHR.Api.Modules.Projects.Entities;

namespace AltomateHR.Api.Modules.Projects;

public class ProjectService : IProjectService
{
    private readonly IProjectRepository _repo;

    public ProjectService(IProjectRepository repo) => _repo = repo;

    public async Task<IEnumerable<ProjectDto>> GetAllAsync() =>
        (await _repo.GetAllAsync()).Select(ToDto);

    public async Task<ProjectDto?> GetByIdAsync(string id)
    {
        var project = await _repo.GetByIdAsync(id);
        return project is null ? null : ToDto(project);
    }

    public async Task<ProjectDto> CreateAsync(SaveProjectDto dto)
    {
        var project = new Project
        {
            Name = dto.Name,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            CreatedAt = DateTime.UtcNow,
            // OrganizationId is auto-stamped by AppDbContext on save.
        };
        await _repo.AddAsync(project);
        return ToDto(project);
    }

    public async Task<ProjectDto?> UpdateAsync(string id, SaveProjectDto dto)
    {
        var project = await _repo.GetByIdAsync(id);
        if (project is null) return null;

        project.Name = dto.Name;
        project.Latitude = dto.Latitude;
        project.Longitude = dto.Longitude;
        await _repo.UpdateAsync(project);
        return ToDto(project);
    }

    public async Task<ProjectDto?> SetArchivedAsync(string id, bool archived)
    {
        var project = await _repo.GetByIdAsync(id);
        if (project is null) return null;

        project.IsArchived = archived;
        await _repo.UpdateAsync(project);
        return ToDto(project);
    }

    private static ProjectDto ToDto(Project p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        XeroProjectId = p.XeroProjectId,
        XeroStatus = p.XeroStatus,
        XeroSyncedAt = p.XeroSyncedAt,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        IsArchived = p.IsArchived,
        CreatedAt = p.CreatedAt,
    };
}
