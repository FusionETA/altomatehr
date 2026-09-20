using AltomateHR.Api.Modules.Projects.Dtos;
using AltomateHR.Api.Modules.Audit;
using AltomateHR.Api.Modules.Projects.Entities;
using AltomateHR.Api.Modules.Teams;

namespace AltomateHR.Api.Modules.Projects;

public class ProjectService : IProjectService
{
    private readonly IProjectRepository _repo;
    private readonly IAuditService _audit;
    private readonly ITeamService _teams;

    public ProjectService(IProjectRepository repo, IAuditService audit, ITeamService teams)
    {
        _repo = repo;
        _audit = audit;
        _teams = teams;
    }

    // Everything, archived included — the admin screens need to see and restore
    // what has been retired.
    public async Task<IEnumerable<ProjectDto>> GetAllAsync() =>
        (await _repo.GetAllAsync()).Select(ToDto);

    // What the clock-in and claim pickers should offer. Archived is excluded
    // for the same reason another team's project is: the server refuses both,
    // so listing them only invites the rejection. Connecting Xero archives the
    // hand-created projects, and those kept showing up here afterwards.
    public async Task<IEnumerable<ProjectDto>> GetForMemberAsync(string userId)
    {
        var mine = (await _teams.GetProjectIdsForMemberAsync(userId)).ToHashSet();
        return (await _repo.GetAllAsync())
            .Where(p => !p.IsArchived && mine.Contains(p.Id))
            .Select(ToDto);
    }

    public async Task<IReadOnlyList<ProjectGeofencePoint>> GetGeofencePointsAsync(string projectId) =>
        await _repo.GetGeofencePointsAsync(projectId);

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
            Location = dto.Location,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            AllowedIps = dto.AllowedIps,
            WorkingHoursStart = dto.WorkingHoursStart,
            WorkingHoursEnd = dto.WorkingHoursEnd,
            WorkingDays = dto.WorkingDays,
            LunchBreakMinutes = dto.LunchBreakMinutes,
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
        project.Location = dto.Location;
        project.Latitude = dto.Latitude;
        project.Longitude = dto.Longitude;
        project.AllowedIps = dto.AllowedIps;
        project.WorkingHoursStart = dto.WorkingHoursStart;
        project.WorkingHoursEnd = dto.WorkingHoursEnd;
        project.WorkingDays = dto.WorkingDays;
        project.LunchBreakMinutes = dto.LunchBreakMinutes;
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
        // Whatever the admin decides here, they now own this row: clearing the
        // flag stops a later disconnect from silently reversing their choice.
        project.ArchivedByXeroConnect = false;
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
        XeroTrackingOptionId = p.XeroTrackingOptionId,
        XeroStatus = p.XeroStatus,
        XeroSyncedAt = p.XeroSyncedAt,
        Location = p.Location,
        Latitude = p.Latitude,
        Longitude = p.Longitude,
        AllowedIps = p.AllowedIps,
        WorkingHoursStart = p.WorkingHoursStart,
        WorkingHoursEnd = p.WorkingHoursEnd,
        WorkingDays = p.WorkingDays,
        LunchBreakMinutes = p.LunchBreakMinutes,
        IsArchived = p.IsArchived,
        CreatedAt = p.CreatedAt,
    };
}
