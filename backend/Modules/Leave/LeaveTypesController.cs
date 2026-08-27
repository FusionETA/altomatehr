using AltomateHR.Api.Modules.Leave.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Leave;

[ApiController]
[Route("leave-types")]
[Authorize]
public class LeaveTypesController : ControllerBase
{
    private readonly ILeaveTypeService _types;

    public LeaveTypesController(ILeaveTypeService types) => _types = types;

    // GET /leave-types — any authenticated user (employees pick a type to apply).
    [RequireScope("leave:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _types.GetAllAsync());

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost]
    public async Task<IActionResult> Create(SaveLeaveTypeDto dto)
    {
        var result = await _types.CreateAsync(dto);
        return result.Ok ? Ok(result.Type) : BadRequest(new { message = result.Error });
    }

    // POST /leave-types/defaults — create any missing default types for this
    // org. Idempotent, so it is safe to call on an org that already has them.
    // GET /leave-types/count — active (non-archived) types in this org.
    [RequireScope("leave:read")]
    [HttpGet("count")]
    public async Task<IActionResult> Count() =>
        Ok(new { active = await _types.CountActiveTypesAsync() });

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("defaults")]
    public async Task<IActionResult> EnsureDefaults() =>
        Ok(new { added = await _types.EnsureDefaultsAsync() });

    [Authorize(Roles = "Admin,Owner")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveLeaveTypeDto dto)
    {
        var result = await _types.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();   // type didn't exist
        return result.Ok ? Ok(result.Type) : BadRequest(new { message = result.Error });
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id) =>
        ToResponse(await _types.SetArchivedAsync(id, true));

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id) =>
        ToResponse(await _types.SetArchivedAsync(id, false));

    // Ok=false with no Error means "no such type" (404); with an Error it's a
    // rule refusal (400) — e.g. UNPAID may not be archived.
    private IActionResult ToResponse(LeaveTypeSaveResult result)
    {
        if (result.Ok) return Ok(result.Type);
        return result.Error is null ? NotFound() : BadRequest(new { message = result.Error });
    }
}
