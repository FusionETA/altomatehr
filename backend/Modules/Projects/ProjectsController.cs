using AltomateHR.Api.Modules.Projects.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Projects;

[ApiController]
[Route("[controller]")]        // → /projects
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projects;

    public ProjectsController(IProjectService projects) => _projects = projects;

    // GET /projects — any authenticated user (employees pick a project when filing claims).
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _projects.GetAllAsync());

    // POST /projects — Admins only.
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(SaveProjectDto dto) =>
        Ok(await _projects.CreateAsync(dto));

    // PUT /projects/{id} — rename (Admins only).
    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveProjectDto dto)
    {
        var project = await _projects.UpdateAsync(id, dto);
        return project is null ? NotFound() : Ok(project);
    }

    // POST /projects/{id}/archive — soft-archive (Admins only).
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        var project = await _projects.SetArchivedAsync(id, true);
        return project is null ? NotFound() : Ok(project);
    }

    // POST /projects/{id}/restore — un-archive (Admins only).
    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var project = await _projects.SetArchivedAsync(id, false);
        return project is null ? NotFound() : Ok(project);
    }
}
