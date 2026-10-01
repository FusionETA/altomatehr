using AltomateHR.Api.Modules.Policies.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Policies;

[ApiController]
[Route("policies")]
[RequireModule(OrgModules.Policies)]
[Authorize(Roles = "Admin,Owner")]   // policies are an admin/owner concern
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policies;

    public PoliciesController(IPolicyService policies) => _policies = policies;

    // A lookup other admin screens borrow (claims / attendance filters, the
    // employee form), so it is held to the plan only. See ModuleGrantExempt.
    [ModuleGrantExempt]
    [RequireScope("policies:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _policies.GetAllAsync());

    [RequireScope("policies:write")]
    [HttpPost]
    public async Task<IActionResult> Create(SavePolicyDto dto)
    {
        var result = await _policies.CreateAsync(dto);
        return result.Ok ? Ok(result.Policy) : BadRequest(new { message = result.Error });
    }

    [RequireScope("policies:write")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SavePolicyDto dto)
    {
        var result = await _policies.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Policy) : BadRequest(new { message = result.Error });
    }

    [RequireScope("policies:write")]
    [HttpPost("{id}/default")]
    public async Task<IActionResult> SetDefault(string id)
    {
        var policy = await _policies.SetDefaultAsync(id);
        return policy is null ? NotFound() : Ok(policy);
    }

    [RequireScope("policies:write")]
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        var policy = await _policies.SetArchivedAsync(id, true);
        return policy is null ? NotFound() : Ok(policy);
    }

    [RequireScope("policies:write")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var policy = await _policies.SetArchivedAsync(id, false);
        return policy is null ? NotFound() : Ok(policy);
    }
}
