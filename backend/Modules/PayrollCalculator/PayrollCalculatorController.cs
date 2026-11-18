using System.Text.Json;
using AltomateHR.Api.Modules.PayrollCalculator.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AltomateHR.Api.Modules.PayrollCalculator;

// POST /payroll-calculator — the payroll calculator on altomate.io's
// payroll outsourcing page. Public: no login, no cookies, called from a
// marketing page. Hence its own CORS policy (altomate.io only), a per-IP rate
// limit, and Cache-Control: no-store. Nothing is stored; request bodies are not
// logged (ApiRequestLogMiddleware keeps route, status and timing only).
//
// No [ApiController]: its automatic 400 is ASP.NET's ProblemDetails, which the
// widget can't place under a field. The body is read raw and validated by the
// service, so every problem comes back as { error: { field, code, message } }.
[Route(RoutePath)]
[AllowAnonymous]
[EnableCors(PolicyName)]
[EnableRateLimiting(PolicyName)]
public class PayrollCalculatorController : ControllerBase
{
    // One name for both the CORS policy and the rate-limit policy (Program.cs).
    public const string PolicyName = "payroll-calculator";

    // No version segment: the widget is the only caller. A breaking change to
    // the request or response has to be released together with web's widget.
    public const string RoutePath = "payroll-calculator";

    private readonly IPayrollCalculatorService _calculator;
    private readonly ILogger<PayrollCalculatorController> _logger;

    public PayrollCalculatorController(IPayrollCalculatorService calculator, ILogger<PayrollCalculatorController> logger)
    {
        _calculator = calculator;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult Calculate([FromBody] JsonElement? body)
    {
        Response.Headers.CacheControl = "no-store";

        // Unreadable JSON leaves the body unbound.
        if (body is null || body.Value.ValueKind == JsonValueKind.Undefined)
        {
            return BadRequest(new PayrollCalculatorErrorBody(new PayrollCalculatorError(
                null, PayrollCalculatorRequestParser.ErrInvalidType, "Send the calculator's fields as JSON.")));
        }

        try
        {
            var outcome = _calculator.Calculate(body.Value);
            return outcome.Error is { } error
                ? BadRequest(new PayrollCalculatorErrorBody(error))
                : Ok(outcome.Result);
        }
        catch (Exception ex)
        {
            // Never a stack trace to a public caller, and never the request body
            // in the log.
            _logger.LogError(ex, "Payroll calculator failed");
            return StatusCode(StatusCodes.Status500InternalServerError, new PayrollCalculatorErrorBody(
                new PayrollCalculatorError(null, "internal", "We couldn't calculate this right now.")));
        }
    }
}
