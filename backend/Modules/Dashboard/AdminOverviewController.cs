using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Dashboard;

// GET /admin/overview — the executive dashboard for the active org. Admin/Owner only.
[ApiController]
[Route("admin/overview")]
[Authorize(Roles = "Admin,Owner")]
public class AdminOverviewController : ControllerBase
{
    private readonly IAdminOverviewService _service;

    public AdminOverviewController(IAdminOverviewService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _service.GetAsync());
}
