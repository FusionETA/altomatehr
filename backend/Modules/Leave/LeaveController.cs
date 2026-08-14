using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AltomateHR.Api.Modules.Leave.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Leave;

[ApiController]
[Route("leave")]
[Authorize]
public class LeaveController : ControllerBase
{
    private readonly ILeaveService _leave;

    public LeaveController(ILeaveService leave) => _leave = leave;

    // GET /leave — the caller's own applications.
    [HttpGet]
    public async Task<IActionResult> GetMine() =>
        Ok(await _leave.GetMineAsync(GetUserId()));

    // GET /leave/team — applications awaiting the caller as the current-step
    // approver. By team seat, so any authenticated user may have a queue.
    [HttpGet("team")]
    public async Task<IActionResult> GetTeam() =>
        Ok(await _leave.GetTeamAsync(GetUserId()));

    // GET /leave/balances — the caller's per-type balances for this year.
    [HttpGet("balances")]
    public async Task<IActionResult> Balances() =>
        Ok(await _leave.GetBalancesAsync(GetUserId()));

    // POST /leave — apply.
    [HttpPost]
    public async Task<IActionResult> Apply(CreateLeaveApplicationDto dto)
    {
        var result = await _leave.ApplyAsync(dto, GetUserId());
        return result.Ok ? Ok(result.Application) : BadRequest(new { message = result.Error });
    }

    // POST /leave/{id}/approve — the current-step approver in the applicant's chain.
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id) =>
        ToTransitionResponse(await _leave.ApproveAsync(id, GetUserId()));

    // POST /leave/{id}/reject — the current-step approver in the applicant's chain.
    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(string id, RejectLeaveDto dto) =>
        ToTransitionResponse(await _leave.RejectAsync(id, GetUserId(), dto.ReviewNotes));

    // POST /leave/{id}/cancel — the owner cancels their own pending request.
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(string id) =>
        ToTransitionResponse(await _leave.CancelAsync(id, GetUserId()));

    private IActionResult ToTransitionResponse(LeaveTransitionResult result)
    {
        if (!result.Found) return NotFound();
        if (!result.Transitioned) return BadRequest(new { message = result.Error });
        return Ok(result.Application);
    }

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");
}
