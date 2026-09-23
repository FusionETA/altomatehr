using AltomateHR.Api.Modules.ApiKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Onboarding;

// Provisioning-time configuration in one call, for an external platform that
// has just created the company and collected the client's answers.
//
// Onboarding-only by design — see OnboardingService for why overtime fans out
// across every policy, and why re-sending this after go-live is the wrong tool.
[ApiController]
[Route("onboarding")]
[Authorize]
public class OnboardingController : ControllerBase
{
    private readonly IOnboardingService _onboarding;

    public OnboardingController(IOnboardingService onboarding) => _onboarding = onboarding;

    // PUT /onboarding
    //
    // 200 every block applied · 409 some applied, some did not · 400 the body
    // was refused before anything ran.
    //
    // The 409 is the unusual one and it is deliberate: this is not atomic, so
    // "partly done" is a real outcome and the caller is told exactly which
    // blocks landed. The fix is to re-send the same body — every block is
    // idempotent, so the ones that already applied simply apply again.
    [RequireScope("organizations:write")]
    [RequireScope("policies:write")]
    [RequireScope("leave:write")]
    [HttpPut]
    public async Task<IActionResult> Apply(OnboardingDto dto)
    {
        var result = await _onboarding.ApplyAsync(dto);

        if (result.ValidationError is not null)
            return BadRequest(new { message = result.ValidationError });

        var body = new
        {
            applied = result.Applied,
            failed = result.Failed,
            policiesUpdated = result.PoliciesUpdated,
            leaveTypesUpdated = result.LeaveTypesUpdated,
        };

        return result.Failed.Count == 0
            ? Ok(body)
            : Conflict(body);
    }
}
