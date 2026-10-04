using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.ApiMonitoring.Dtos;
using AltomateHR.Api.Modules.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.ApiMonitoring;

// How the API is behaving, across every company. Fusioneta superadmins only —
// it names companies and shows their failures to one another's operators
// otherwise — and never to a machine key, same gate as ApiKeysController.
[ApiController]
[HumanOnly]
[Route("platform/api-monitoring")]
[Authorize(Policy = AuthPolicies.Superadmin)]
public class ApiMonitoringController : ControllerBase
{
    private readonly IApiMonitoringService _monitoring;

    public ApiMonitoringController(IApiMonitoringService monitoring) => _monitoring = monitoring;

    // GET /platform/api-monitoring/summary?from&to&organizationId
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? organizationId) =>
        Ok(await _monitoring.GetSummaryAsync(from, to, organizationId));

    // GET /platform/api-monitoring/errors?from&to&route&organizationId&status&limit
    [HttpGet("errors")]
    public async Task<IActionResult> Errors([FromQuery] ApiRequestErrorQuery query) =>
        Ok(await _monitoring.GetRecentErrorsAsync(query));
}
