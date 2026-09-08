using AltomateHR.Api.Modules.Projects.Dtos;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Projects.Entities;

namespace AltomateHR.Api.Modules.Projects;

public class ProjectService : IProjectService
{
    private readonly IProjectRepository _repo;
    private readonly IAuditService _audit;

    public ProjectService(IProjectRepository repo, IAuditService audit)
    {
        _repo = repo;
        _audit = audit;
    }

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
            AllowedIps = dto.AllowedIps,
            CreatedAt = DateTime.UtcNow,
            // OrganizationId is auto-stamped by AppDbContext on save.
        };
        await _repo.AddAsync(project);

        await _audit.WriteAsync(new AuditEvent(
            AuditActions.ProjectCreate,
            project.Name,
            TargetType: "Project",
            TargetId: project.Id,
            Metadata: new { project.Name, project.Latitude, project.Longitude }));

        return ToDto(project);
    }

    public async Task<ProjectDto?> UpdateAsync(string id, SaveProjectDto dto)
    {
        var project = await _repo.GetByIdAsync(id);
        if (project is null) return null;

        project.Name = dto.Name;
        project.Latitude = dto.Latitude;
        project.Longitude = dto.Longitude;
        project.AllowedIps = dto.AllowedIps;
        await _repo.UpdateAsync(project);

        // The geofence centre and the IP allowlist both decide whether an
        // attendance clock-in is accepted, so a change to either is worth being
        // able to place on a timeline.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.ProjectUpdate,
            project.Name,
            TargetType: "Project",
            TargetId: project.Id,
            Metadata: new { project.Name, project.Latitude, project.Longitude, project.AllowedIps }));

        return ToDto(project);
    }

    public async Task<ProjectDto?> SetArchivedAsync(string id, bool archived)
    {
        var project = await _repo.GetByIdAsync(id);
        if (project is null) return null;

        project.IsArchived = archived;
        await _repo.UpdateAsync(project);

        await _audit.WriteAsync(new AuditEvent(
            archived ? AuditActions.ProjectArchive : AuditActions.ProjectRestore,
            project.Name,
            TargetType: "Project",
            TargetId: project.Id));

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
        AllowedIps = p.AllowedIps,
        IsArchived = p.IsArchived,
        CreatedAt = p.CreatedAt,
    };
}
