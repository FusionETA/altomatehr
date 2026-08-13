using System.Security.Claims;
using AltomateHR.Api.Modules.Organizations.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Organizations;

[ApiController]
[Route("[controller]")]        // → /organizations
[Authorize]
public class OrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizations;

    public OrganizationsController(IOrganizationService organizations) => _organizations = organizations;

    // GET /organizations/current — the caller's own org (any authenticated user can read it).
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent()
    {
        var org = await _organizations.GetByIdAsync(GetOrgId());
        return org is null ? NotFound() : Ok(org);
    }

    // PUT /organizations/current — update org settings (Admins only).
    [Authorize(Roles = "Admin")]
    [HttpPut("current")]
    public async Task<IActionResult> UpdateCurrent(UpdateOrganizationDto dto)
    {
        var org = await _organizations.UpdateAsync(GetOrgId(), dto);
        return org is null ? NotFound() : Ok(org);
    }

    // The org id comes from the JWT 'org' claim — never from the client.
    private string GetOrgId() =>
        User.FindFirstValue("org")
        ?? throw new InvalidOperationException("Organization claim is missing.");
}
