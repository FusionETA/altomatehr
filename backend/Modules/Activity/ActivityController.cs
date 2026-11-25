using AltomateHR.Api.Modules.Activity.Dtos;
using AltomateHR.Api.Modules.ApiKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Activity;

// Where the browser tracker sends what a signed-in person opens and clicks.
// Any signed-in person may SEND; only Fusioneta reads it back
// (PlatformActivityController). Never a machine key.
[ApiController]
[HumanOnly]
[Route("activity")]
[Authorize]
public class ActivityController : ControllerBase
{
    private readonly IActivityService _activity;

    public ActivityController(IActivityService activity) => _activity = activity;

    // POST /activity/events — a batch of page views and clicks; an empty batch
    // is a heartbeat. Always 204: the tracker has nothing to do with an answer,
    // and invalid events are dropped rather than reported.
    [HttpPost("events")]
    public async Task<IActionResult> Events(ActivityBatchDto batch)
    {
        await _activity.IngestAsync(batch);
        return NoContent();
    }
}
