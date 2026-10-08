using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Documents.Dtos;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Documents;

// HR letters: the company's template library, and letters generated from it
// for one employee at a time.
//
// Admin-only, all of it — including the letters kept on an employee's file.
// Employees never reach this controller, and nothing here feeds the employee
// documents list in their portal.
[ApiController]
[Route("documents")]
[RequireModule(OrgModules.Documents)]
[Authorize(Roles = "Admin,Owner")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentTemplateService _templates;
    private readonly IGeneratedLetterService _letters;

    public DocumentsController(IDocumentTemplateService templates, IGeneratedLetterService letters)
    {
        _templates = templates;
        _letters = letters;
    }

    // ─── Merge fields ────────────────────────────────────────────────────

    // The field registry the editor's side panel lists.
    [RequireScope("documents:read")]
    [HttpGet("merge-fields")]
    public IActionResult MergeFields() => Ok(_templates.GetMergeFields());

    // ─── Templates ───────────────────────────────────────────────────────

    [RequireScope("documents:read")]
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates() => Ok(await _templates.GetAllAsync());

    [RequireScope("documents:read")]
    [HttpGet("templates/{id}")]
    public async Task<IActionResult> GetTemplate(string id) =>
        await _templates.GetAsync(id) is { } template ? Ok(template) : NotFound();

    [RequireScope("documents:write")]
    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate(SaveDocumentTemplateDto dto) =>
        TemplateResult(await _templates.CreateAsync(dto));

    [RequireScope("documents:write")]
    [HttpPut("templates/{id}")]
    public async Task<IActionResult> UpdateTemplate(string id, SaveDocumentTemplateDto dto) =>
        TemplateResult(await _templates.UpdateAsync(id, dto));

    [RequireScope("documents:write")]
    [HttpDelete("templates/{id}")]
    public async Task<IActionResult> DeleteTemplate(string id) =>
        await _templates.DeleteAsync(id) ? NoContent() : NotFound();

    // Adds whichever sample letters the company doesn't already have.
    [RequireScope("documents:write")]
    [HttpPost("templates/samples")]
    public async Task<IActionResult> AddSamples() => Ok(await _templates.AddSamplesAsync());

    // ─── Preview / resolve / generate ────────────────────────────────────

    // An unsaved template (the editor before its first save).
    [RequireScope("documents:read")]
    [ReadOnlyAction]   // renders a PDF; changes nothing
    [HttpPost("templates/preview")]
    public async Task<IActionResult> PreviewDraft(PreviewLetterDto dto) =>
        FileOrError(await _letters.PreviewAsync(null, dto));

    [RequireScope("documents:read")]
    [ReadOnlyAction]
    [HttpPost("templates/{id}/preview")]
    public async Task<IActionResult> Preview(string id, PreviewLetterDto dto) =>
        FileOrError(await _letters.PreviewAsync(id, dto));

    // What the template needs for this employee, filled from the records,
    // with every gap flagged — what the generate dialog shows.
    //
    // Resolve, an employee preview and Generate print the employee's record
    // (IC, address, salary), so the service also requires Employees at View
    // (employees:read for a key) — and Manage (employees:write) to save gaps
    // back to the record. [RequireModule] above only covers Documents.
    [RequireScope("documents:read")]
    [ReadOnlyAction]
    [HttpPost("templates/{id}/resolve")]
    public async Task<IActionResult> Resolve(string id, ResolveLetterDto dto)
    {
        var result = await _letters.ResolveAsync(id, dto.EmployeeUserId);
        if (result.Forbidden) return AccessGate.Forbidden(result.Error!);
        if (!result.Ok) return result.Error is null ? NotFound() : BadRequest(new { message = result.Error });
        return Ok(result.Letter);
    }

    // The letter itself. Not a [ReadOnlyAction]: it can keep a copy on file
    // and write gaps back to the employee record.
    [RequireScope("documents:write")]
    [HttpPost("templates/{id}/generate")]
    public async Task<IActionResult> Generate(string id, GenerateLetterDto dto) =>
        FileOrError(await _letters.GenerateAsync(id, dto));

    // ─── Letters on file ─────────────────────────────────────────────────

    [RequireScope("documents:read")]
    [HttpGet("generated")]
    public async Task<IActionResult> GetGenerated([FromQuery] string? employeeUserId)
    {
        // Letters on file print the employee's record: the service also
        // requires Employees at View (employees:read) for list, file and delete.
        var result = await _letters.ListAsync(employeeUserId);
        return result.Forbidden ? AccessGate.Forbidden(result.Error!) : Ok(result.Letters);
    }

    [RequireScope("documents:read")]
    [HttpGet("generated/{id}/file")]
    public async Task<IActionResult> DownloadGenerated(string id) =>
        FileOrError(await _letters.DownloadAsync(id));

    [RequireScope("documents:write")]
    [HttpDelete("generated/{id}")]
    public async Task<IActionResult> DeleteGenerated(string id)
    {
        var result = await _letters.DeleteAsync(id);
        if (result.Forbidden) return AccessGate.Forbidden(result.Error!);
        return result.Ok ? NoContent() : NotFound();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private IActionResult TemplateResult(TemplateSaveResult result)
    {
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok
            ? Ok(result.Template)
            : BadRequest(new { message = result.Error, unknownFields = result.UnknownFields });
    }

    private IActionResult FileOrError(LetterFileResult result)
    {
        if (result.Forbidden) return AccessGate.Forbidden(result.Error!);
        if (!result.Ok)
            return result.Error is null
                ? NotFound()
                : BadRequest(new { message = result.Error, missingFields = result.MissingFields });

        Response.Headers.CacheControl = "no-store";
        return File(result.Content!, result.ContentType!, result.FileName);
    }
}
