using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Shifts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Shifts;

[ApiController]
[Route("shifts")]
[Authorize(Roles = "Admin,Owner")]
public class ShiftsController : ControllerBase
{
    private readonly IShiftService _shifts;

    public ShiftsController(IShiftService shifts) => _shifts = shifts;

    // GET /shifts — every shift in the org. Pass ?projectId= to scope to one project.
    [RequireScope("attendance:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? projectId) =>
        Ok(projectId is null ? await _shifts.GetAllAsync() : await _shifts.GetForProjectAsync(projectId));

    [HttpPost]
    public async Task<IActionResult> Create(CreateShiftDto dto)
    {
        var result = await _shifts.CreateAsync(dto);
        return result.Ok ? Ok(result.Shift) : BadRequest(new { message = result.Error });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateShiftDto dto)
    {
        var result = await _shifts.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Shift) : BadRequest(new { message = result.Error });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _shifts.DeleteAsync(id);
        if (result.Ok) return NoContent();
        if (result.Code == "NOT_FOUND") return NotFound();
        return BadRequest(new { message = result.Error, code = result.Code, assignedCount = result.AssignedCount });
    }

    [HttpPost("{id}/default")]
    public async Task<IActionResult> SetDefault(string id)
    {
        var result = await _shifts.SetDefaultAsync(id);
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Shift) : BadRequest(new { message = result.Error });
    }
}
