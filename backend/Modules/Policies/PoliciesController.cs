using AltomateHR.Api.Modules.Policies.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Policies;

[ApiController]
[Route("policies")]
// Who may do what is the service's (PolicyService / AdminAccess): every admin
// edits the policies their employee scope covers; creating, archiving and the
// default — which reach the whole company — are the Owner's. Policies isn't a
// per-admin grant (OrgModules.GrantableModules), so this checks the plan only.
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
    public async Task<IActionResult> GetAll() => Ok(await _policies.GetVisibleAsync());

    [RequireScope("policies:write")]
    [HttpPost]
    public async Task<IActionResult> Create(SavePolicyDto dto)
    {
        return ToResponse(await _policies.CreateAsync(dto));
    }

    [RequireScope("policies:write")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SavePolicyDto dto)
    {
        return ToResponse(await _policies.UpdateAsync(id, dto));
    }

    [RequireScope("policies:write")]
    [HttpPost("{id}/default")]
    public async Task<IActionResult> SetDefault(string id)
    {
        return ToResponse(await _policies.SetDefaultAsync(id));
    }

    [RequireScope("policies:write")]
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        return ToResponse(await _policies.SetArchivedAsync(id, true));
    }

    [RequireScope("policies:write")]
    [HttpPost("{id}/restore")]
    public async Task<IActionResult> Restore(string id)
    {
        return ToResponse(await _policies.SetArchivedAsync(id, false));
    }

    private IActionResult ToResponse(PolicySaveResult result) =>
        result.Ok ? Ok(result.Policy)
        : result.Forbidden ? StatusCode(StatusCodes.Status403Forbidden,
            new { error = new { status = 403, message = result.Error } })
        : result.Error is null ? NotFound()
        : BadRequest(new { message = result.Error });
}
