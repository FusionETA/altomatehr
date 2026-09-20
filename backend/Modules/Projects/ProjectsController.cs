using AltomateHR.Api.Common;
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

    private readonly ICurrentUser _currentUser;

    public ProjectsController(IProjectService projects, ICurrentUser currentUser)
    {
        _projects = projects;
        _currentUser = currentUser;
    }

    // GET /projects/my-ip — the IP the server currently sees for this caller.
    // Lets an admin drop "the machine I'm on right now" into a project's
    // allowlist without having to look it up, and it's the SAME value the
    // clock-in IP check compares against, so it will actually match.
    [Authorize(Roles = "Admin,Owner")]
    [HttpGet("my-ip")]
    public IActionResult MyIp() =>
        Ok(new { ip = _currentUser.IpAddress ?? HttpContext.Connection.RemoteIpAddress?.ToString() });

    // GET /projects — any authenticated user (employees pick a project when filing claims).
    [RequireScope("projects:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _projects.GetAllAsync());

    // GET /projects/{id} — one project, WITH its geofence sites and allowlist
    // entries. The list endpoint above omits both: it renders a grid of names,
    // and loading every project's child rows to draw that would be a query per
    // project for data the screen never shows.
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var project = await _projects.GetByIdAsync(id);
        return project is null ? NotFound() : Ok(project);
    }

    // GET /projects/mine — the caller's own projects, via their team
    // memberships. What the clock-in picker should offer: clocking into a
    // project you're not on is refused, so listing them all only invites the
    // rejection.
    [RequireScope("projects:read")]
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine() =>
        Ok(await _projects.GetForMemberAsync(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty));

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
        try
        {
            var project = await _projects.UpdateAsync(id, dto);
            return project is null ? NotFound() : Ok(project);
        }
        catch (ArgumentException ex)
        {
            // A malformed allowlist entry is the admin's typo, not a fault:
            // 400 with the offending value, not the global handler's 500.
            // ValidationProblem so the message lands in `errors`, which is
            // where the client reads the real reason from.
            return ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["allowedIpEntries"] = [ex.Message] }));
        }
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
