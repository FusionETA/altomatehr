using AltomateHR.Api.Modules.Xero.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.Xero;

[ApiController]
[Route("xero")]
public class XeroController : ControllerBase
{
    private readonly IXeroService _xero;
    private readonly ILogger<XeroController> _logger;
    private readonly XeroOptions _options;

    public XeroController(IXeroService xero, ILogger<XeroController> logger, IOptions<XeroOptions> options)
    {
        _xero = xero;
        _logger = logger;
        _options = options.Value;
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpGet("status")]
    public async Task<ActionResult<XeroStatusDto>> Status() => Ok(await _xero.GetStatusAsync());

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("connect-url")]
    public async Task<ActionResult<XeroConnectUrlDto>> ConnectUrl([FromQuery] string? returnUrl = null) =>
        Ok(await _xero.CreateConnectUrlAsync(returnUrl));

    [AllowAnonymous]
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state)
    {
        try
        {
            var redirectUrl = await _xero.CompleteCallbackAsync(code ?? string.Empty, state ?? string.Empty);
            return Redirect(redirectUrl);
        }
        catch (Exception ex) when (ex is XeroConfigurationException or XeroConnectionException)
        {
            _logger.LogWarning(ex, "Xero callback failed.");
            return Redirect(_options.FailureRedirectUrl);
        }
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("disconnect")]
    public async Task<IActionResult> Disconnect()
    {
        await _xero.DisconnectAsync();
        return NoContent();
    }

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("sync-accounts")]
    public async Task<ActionResult<XeroSyncAccountsResultDto>> SyncAccounts() =>
        Ok(await _xero.SyncAccountsAsync());

    [Authorize(Roles = "Admin,Owner")]
    [HttpPost("sync-projects")]
    public async Task<ActionResult<XeroSyncProjectsResultDto>> SyncProjects() =>
        Ok(await _xero.SyncProjectsAsync());
}
