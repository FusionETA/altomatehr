using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.Projects.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.Projects;

public class ProjectRepository : IProjectRepository
{
    private readonly AppDbContext _db;

    public ProjectRepository(AppDbContext db) => _db = db;

    // Auto-scoped to the current org by the global query filter.
    public Task<List<Project>> GetAllAsync() =>
        _db.Projects.OrderBy(p => p.Name).ToListAsync();

    public Task<Project?> GetByIdAsync(string id) =>
        _db.Projects.FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Project> AddAsync(Project project)
    {
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
        return project;
    }

    public async Task UpdateAsync(Project project)
    {
        _db.Projects.Update(project);
        await _db.SaveChangesAsync();
    }

    // Ordered here, once, so no caller has to remember that the order decides
    // which site a clock-in is matched against.
    public Task<List<ProjectGeofencePoint>> GetGeofencePointsAsync(string projectId) =>
        _db.ProjectGeofencePoints
            .Where(g => g.ProjectId == projectId)
            .OrderBy(g => g.SortOrder)
            .ToListAsync();

    // Unordered on purpose: unlike the geofence sites, matching an allowlist is
    // "does ANY entry cover this address", so the order carries no meaning.
    public Task<List<ProjectAllowedIp>> GetAllowedIpsAsync(string projectId) =>
        _db.ProjectAllowedIps.Where(a => a.ProjectId == projectId).ToListAsync();

    // Delete-then-insert rather than diffing by id: the lists are a handful of
    // rows an admin retypes freely, and reconciling them by id would mostly be
    // a way to get the SortOrder subtly wrong. SaveChangesAsync runs both
    // halves in one transaction.
    public async Task ReplaceGeofencePointsAsync(
        string projectId, IReadOnlyList<ProjectGeofencePoint> points)
    {
        _db.ProjectGeofencePoints.RemoveRange(
            await _db.ProjectGeofencePoints.Where(g => g.ProjectId == projectId).ToListAsync());
        _db.ProjectGeofencePoints.AddRange(points);
        await _db.SaveChangesAsync();   // OrganizationId auto-stamped here
    }

    public Task<Dictionary<string, int>> GetGeofencePointCountsAsync() =>
        _db.ProjectGeofencePoints
            .GroupBy(g => g.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count);

    public Task<Dictionary<string, int>> GetAllowedIpCountsAsync() =>
        _db.ProjectAllowedIps
            .GroupBy(a => a.ProjectId)
            .Select(g => new { ProjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProjectId, x => x.Count);

    public async Task ReplaceAllowedIpsAsync(
        string projectId, IReadOnlyList<ProjectAllowedIp> entries)
    {
        _db.ProjectAllowedIps.RemoveRange(
            await _db.ProjectAllowedIps.Where(a => a.ProjectId == projectId).ToListAsync());
        _db.ProjectAllowedIps.AddRange(entries);
        await _db.SaveChangesAsync();
    }
}
