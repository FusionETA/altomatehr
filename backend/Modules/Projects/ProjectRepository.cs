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
}
