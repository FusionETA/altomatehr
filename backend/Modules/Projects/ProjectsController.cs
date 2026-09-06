using AltomateHR.Api.Modules.Teams;
using System.Security.Claims;
using AltomateHR.Api.Modules.Projects.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Projects;

[ApiController]
[Route("[controller]")]        // → /projects
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectService _projects;

    private readonly ITeamService _teams;

    public ProjectsController(IProjectService projects, ITeamService teams)
    {
        _projects = projects;
        _teams = teams;
    }

    // GET /projects — any authenticated user (employees pick a project when filing claims).
    [RequireScope("projects:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _projects.GetAllAsync());

    // GET /projects/mine — the caller's own projects, via their team
    // memberships. What the clock-in picker should offer: clocking into a
    // project you're not on is refused, so listing them all only invites the
    // rejection.
    [RequireScope("projects:read")]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var mine = (await _teams.GetProjectIdsForMemberAsync(userId)).ToHashSet();
        var all = await _projects.GetAllAsync();
        return Ok(all.Where(p => mine.Contains(p.Id)));
    }

    // POST /projects — Admins only.
    [Authorize(Roles = "Admin,Owner")]
    [HttpPost]
    public async Task<IActionResult> Create(SaveProjectDto dto) =>
        Ok(await _projects.CreateAsync(dto));

    // PUT /projects/{id} — rename (Admins only).
    [Authorize(Roles = "Admin,Owner")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveProjectDto dto)
    {
        var project = await _projects.UpdateAsync(id, dto);
        return project is null ? NotFound() : Ok(project);
    }

    // POST /projects/{id}/archive — soft-archive (Admins only).
    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        var project = await _projects.SetArchivedAsync(id, true);
        return project is null ? NotFound() : Ok(project);
    }

    // POST /projects/{id}/restore — un-archive (Admins only).
    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var project = await _projects.SetArchivedAsync(id, false);
        return project is null ? NotFound() : Ok(project);
    }
}
