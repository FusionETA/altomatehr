using AltomateHR.Api.Modules.Payroll.Dtos;
using AltomateHR.Api.Modules.Payroll.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Payroll;

// Saved logins for the statutory portals.
//
// ⚠️ `reveal` returns a password in clear. Admin/Owner only, and audited —
// there is no employee-facing read of any kind here.
[ApiController]
[Route("payroll/portal-credentials")]
[Authorize(Roles = "Admin,Owner")]
public class PortalCredentialsController : ControllerBase
{
    private readonly IPortalCredentialService _credentials;

    public PortalCredentialsController(IPortalCredentialService credentials) =>
        _credentials = credentials;

    // Every portal, with passwords masked.
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _credentials.GetAllAsync());

    // The password in clear. A separate endpoint behind an explicit click so
    // it is never fetched just by opening the page.
    [HttpGet("{portal}/reveal")]
    public async Task<IActionResult> Reveal(PortalKind portal)
    {
        var credential = await _credentials.RevealAsync(portal);
        return credential is null ? NotFound() : Ok(credential);
    }

    [HttpPut("{portal}")]
    public async Task<IActionResult> Save(PortalKind portal, SavePortalCredentialDto dto) =>
        Ok(await _credentials.SaveAsync(portal, dto));

    [HttpDelete("{portal}")]
    public async Task<IActionResult> Delete(PortalKind portal) =>
        await _credentials.DeleteAsync(portal) ? NoContent() : NotFound();
}
