using AltomateHR.Api.Modules.Policies.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Policies;

[ApiController]
[Route("policies")]
[Authorize(Roles = "Admin,Owner")]   // policies are an admin/owner concern
public class PoliciesController : ControllerBase
{
    private readonly IPolicyService _policies;

    public PoliciesController(IPolicyService policies) => _policies = policies;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _policies.GetAllAsync());

    [HttpPost]
    public async Task<IActionResult> Create(SavePolicyDto dto)
    {
        var result = await _policies.CreateAsync(dto);
        return result.Ok ? Ok(result.Policy) : BadRequest(new { message = result.Error });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SavePolicyDto dto)
    {
        var result = await _policies.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Policy) : BadRequest(new { message = result.Error });
    }

    [HttpPost("{id}/default")]
    public async Task<IActionResult> SetDefault(string id)
    {
        var policy = await _policies.SetDefaultAsync(id);
        return policy is null ? NotFound() : Ok(policy);
    }

    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        var policy = await _policies.SetArchivedAsync(id, true);
        return policy is null ? NotFound() : Ok(policy);
    }

    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        var policy = await _policies.SetArchivedAsync(id, false);
        return policy is null ? NotFound() : Ok(policy);
    }
}
