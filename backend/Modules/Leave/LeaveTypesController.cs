using AltomateHR.Api.Modules.Leave.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Leave;

[ApiController]
[Route("leave-types")]
[Authorize]
public class LeaveTypesController : ControllerBase
{
    private readonly ILeaveTypeService _types;

    public LeaveTypesController(ILeaveTypeService types) => _types = types;

    // GET /leave-types — any authenticated user (employees pick a type to apply).
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _types.GetAllAsync());

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> Create(SaveLeaveTypeDto dto)
    {
        var result = await _types.CreateAsync(dto);
        return result.Ok ? Ok(result.Type) : BadRequest(new { message = result.Error });
    }

    [Authorize(Roles = "Admin")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveLeaveTypeDto dto)
    {
        var result = await _types.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();   // type didn't exist
        return result.Ok ? Ok(result.Type) : BadRequest(new { message = result.Error });
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        var type = await _types.SetArchivedAsync(id, true);
        return type is null ? NotFound() : Ok(type);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var type = await _types.SetArchivedAsync(id, false);
        return type is null ? NotFound() : Ok(type);
    }
}
