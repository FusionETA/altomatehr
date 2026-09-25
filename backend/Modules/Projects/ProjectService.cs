using AltomateHR.Api.Common;
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
    private readonly Xero.IProjectTrackingScope _trackingScope;

    public ProjectService(
        IProjectRepository repo, IAuditService audit, ITeamService teams,
        Xero.IProjectTrackingScope trackingScope)
    {
        _trackingScope = trackingScope;
        _repo = repo;
        _audit = audit;
        _teams = teams;
    }

    // Everything, archived included — the admin screens need to see and restore
    // what has been retired.
    public async Task<IEnumerable<ProjectDto>> GetAllAsync()
    {
        var projects = await _repo.GetAllAsync();
        // Two grouped queries for the whole org, not one pair per project.
        var sites = await _repo.GetGeofencePointCountsAsync();
        var ips = await _repo.GetAllowedIpCountsAsync();
        var activeCategory = await _trackingScope.GetActiveCategoryIdAsync();

        // Every project is still returned — other modules look names up in
        // this list for past records — but ones from a switched-out tracking
        // category are flagged, so screens that PICK a project can leave them
        // out.
        return projects.Select(p =>
        {
            var dto = ToDto(p);
            dto.GeofenceSiteCount = sites.GetValueOrDefault(p.Id);
            dto.AllowedIpCount = ips.GetValueOrDefault(p.Id);
            dto.HiddenByTrackingCategory = Xero.ProjectTrackingVisibility.IsHidden(
                p.XeroTrackingOptionId, p.XeroTrackingCategoryId, activeCategory);
            return dto;
        });
    }

    // What the clock-in and claim pickers should offer. Archived is excluded
    // for the same reason another team's project is: the server refuses both,
    // so listing them only invites the rejection. Connecting Xero archives the
    // hand-created projects, and those kept showing up here afterwards.
    public async Task<IEnumerable<ProjectDto>> GetForMemberAsync(string userId)
    {
        var mine = (await _teams.GetProjectIdsForMemberAsync(userId)).ToHashSet();
        var activeCategory = await _trackingScope.GetActiveCategoryIdAsync();

        // Also leaves out projects from a Xero tracking category the admin has
        // switched away from: after switching "Project" → "Region", offering
        // both sets would put regions and projects in one picker.
        return (await _repo.GetAllAsync())
            .Where(p => !p.IsArchived && mine.Contains(p.Id))
            .Where(p => !Xero.ProjectTrackingVisibility.IsHidden(
                p.XeroTrackingOptionId, p.XeroTrackingCategoryId, activeCategory))
            .Select(ToDto);
    }

    public async Task<IReadOnlyList<ProjectGeofencePoint>> GetGeofencePointsAsync(string projectId) =>
        await _repo.GetGeofencePointsAsync(projectId);

    public async Task<IReadOnlyList<ProjectAllowedIp>> GetAllowedIpsAsync(string projectId) =>
        await _repo.GetAllowedIpsAsync(projectId);

    public async Task<ProjectDto?> GetByIdAsync(string id)
    {
        var project = await _repo.GetByIdAsync(id);
        return project is null ? null : await ToDtoWithListsAsync(project);
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

        // Rejected before anything is written, not dropped at match time: a
        // silently discarded entry looks saved on the settings screen while the
        // network it names is quietly not allowed.
        var badEntry = dto.AllowedIpEntries.FirstOrDefault(e => !IpAllowlist.IsValidEntry(e.Cidr));
        if (badEntry is not null)
            throw new ArgumentException(
                $"\"{badEntry.Cidr}\" is not a valid IPv4 address or CIDR range.");

        await _repo.UpdateAsync(project);

        // SortOrder is assigned from the submitted order — the sites are walked
        // in it and the first inside the radius wins, so dragging a site up the
        // list is a change to enforcement, not to presentation.
        await _repo.ReplaceGeofencePointsAsync(project.Id, dto.GeofencePoints.Select((g, i) =>
            new ProjectGeofencePoint
            {
                OrganizationId = project.OrganizationId,
                ProjectId = project.Id,
                Label = g.Label.Trim(),
                Latitude = g.Latitude,
                Longitude = g.Longitude,
                SortOrder = i,
            }).ToList());

        await _repo.ReplaceAllowedIpsAsync(project.Id, dto.AllowedIpEntries.Select(e =>
            new ProjectAllowedIp
            {
                OrganizationId = project.OrganizationId,
                ProjectId = project.Id,
                Label = e.Label.Trim(),
                Cidr = e.Cidr.Trim(),
            }).ToList());

        // The geofence centre and the IP allowlist both decide whether an
        // attendance clock-in is accepted, so a change to either is worth being
        // able to place on a timeline.
        await _audit.WriteAsync(new AuditEvent(
            AuditActions.ProjectUpdate,
            project.Name,
            TargetType: "Project",
            TargetId: project.Id,
            Metadata: new
            {
                project.Name,
                project.Latitude,
                project.Longitude,
                project.AllowedIps,
                GeofenceSites = dto.GeofencePoints.Count,
                AllowedIpEntries = dto.AllowedIpEntries.Count,
            }));

        return await ToDtoWithListsAsync(project);
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

    // The lists are attached only where a caller needs them (GetByIdAsync and
    // UpdateAsync). GetAllAsync deliberately doesn't: it renders a grid of
    // names, and loading every project's sites to draw that would be a query
    // per project for data the screen never shows.
    private async Task<ProjectDto> ToDtoWithListsAsync(Project p)
    {
        var dto = ToDto(p);
        dto.GeofencePoints = (await _repo.GetGeofencePointsAsync(p.Id))
            .Select(g => new GeofencePointDto
            {
                Id = g.Id, Label = g.Label, Latitude = g.Latitude, Longitude = g.Longitude,
            }).ToList();
        dto.AllowedIpEntries = (await _repo.GetAllowedIpsAsync(p.Id))
            .Select(a => new AllowedIpDto { Id = a.Id, Label = a.Label, Cidr = a.Cidr })
            .ToList();
        // The counts the list view reads. Left at 0 here, the project returned
        // by a SAVE claimed no sites — and since saving also clears the legacy
        // lat/long pair, the card said "No geofence" right after one was added.
        dto.GeofenceSiteCount = dto.GeofencePoints.Count;
        dto.AllowedIpCount = dto.AllowedIpEntries.Count;
        return dto;
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
