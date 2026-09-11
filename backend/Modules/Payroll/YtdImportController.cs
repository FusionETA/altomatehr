using AltomateHR.Api.Common.Tabular;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Payroll;

// Seeding payroll history when an org migrates mid-year.
//
// Admin-only, and deliberately two-step: preview, then import. Writing a
// year of payslips off an uploaded file without showing the admin the match
// list first is how the wrong people get someone else's history.
[ApiController]
[Route("payroll/ytd-import")]
[Authorize(Roles = "Admin,Owner")]
public class YtdImportController : ControllerBase
{
    private readonly IYtdImportService _import;

    public YtdImportController(IYtdImportService import) => _import = import;

    [HttpGet("template/{year:int}")]
    public async Task<IActionResult> Template(int year, [FromQuery] TabularFormat format = TabularFormat.Xlsx)
    {
        var result = await _import.BuildTemplateAsync(year, format);
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpPost("{year:int}/preview")]
    public async Task<IActionResult> Preview(int year, IFormFile file) =>
        await WithUpload(file, async (content, format) =>
        {
            var preview = await _import.PreviewAsync(year, content, format);
            return preview.Ok ? Ok(preview) : BadRequest(preview);
        });

    [HttpPost("{year:int}")]
    public async Task<IActionResult> Import(int year, IFormFile file) =>
        await WithUpload(file, async (content, format) =>
        {
            var result = await _import.ImportAsync(year, content, format);
            return result.Ok ? Ok(result) : BadRequest(result);
        });

    private static async Task<IActionResult> WithUpload(
        IFormFile? file, Func<byte[], TabularFormat, Task<IActionResult>> act)
    {
        if (file is null || file.Length == 0)
        {
            return new BadRequestObjectResult(new { error = "No file was uploaded." });
        }

        var format = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".csv" => TabularFormat.Csv,
            _ => TabularFormat.Xlsx,
        };

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);

        return await act(buffer.ToArray(), format);
    }
}
