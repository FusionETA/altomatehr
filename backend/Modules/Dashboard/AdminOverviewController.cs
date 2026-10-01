using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;

namespace AltomateHR.Api.Modules.Dashboard;

// GET /admin/overview — the executive dashboard for the active org. Admin/Owner only.
[ApiController]
[Route("admin/overview")]
[Authorize(Roles = "Admin,Owner")]
public class AdminOverviewController : ControllerBase
{
    private readonly IAdminOverviewService _service;

    public AdminOverviewController(IAdminOverviewService service) => _service = service;

    [HumanOnly]
    [HttpGet]
    public async Task<IActionResult> Get() => Ok(await _service.GetAsync());
}
