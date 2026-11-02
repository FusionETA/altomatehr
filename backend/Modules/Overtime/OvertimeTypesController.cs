using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Overtime.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Overtime;

// The company's overtime types: three built in, plus its own. Set up beside
// policies (each policy can override a type's multiplier), so it is held to
// the same plan and roles; changing them is the Owner's (the service decides).
[ApiController]
[Route("overtime-types")]
[RequireModule(OrgModules.Policies)]
[Authorize(Roles = "Admin,Owner")]
public class OvertimeTypesController : ControllerBase
{
    private readonly IOvertimeTypeService _types;

    public OvertimeTypesController(IOvertimeTypeService types) => _types = types;

    // GET /overtime-types?includeArchived=true — the policy form reads it too.
    [ModuleGrantExempt]
    [RequireScope("policies:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeArchived = false) =>
        Ok(await _types.GetAllAsync(includeArchived));

    [RequireScope("policies:write")]
    [HttpPost]
    public async Task<IActionResult> Create(SaveOvertimeTypeDto dto) => ToResponse(await _types.CreateAsync(dto));

    [RequireScope("policies:write")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveOvertimeTypeDto dto) =>
        ToResponse(await _types.UpdateAsync(id, dto));

    [RequireScope("policies:write")]
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id) => ToResponse(await _types.ArchiveAsync(id));

    private IActionResult ToResponse(OvertimeTypeSaveResult result) =>
        result.Ok ? Ok(result.Type)
        : result.Forbidden ? StatusCode(StatusCodes.Status403Forbidden,
            new { error = new { status = 403, message = result.Error } })
        : result.NotFound ? NotFound()
        : BadRequest(new { message = result.Error });
}
