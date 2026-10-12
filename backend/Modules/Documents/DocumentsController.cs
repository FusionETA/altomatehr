using AltomateHR.Api.Modules.Ai;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.Documents.Dtos;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    private readonly ITemplateAiService _ai;

    public DocumentsController(IDocumentTemplateService templates, IGeneratedLetterService letters, ITemplateAiService ai)
    {
        _templates = templates;
        _letters = letters;
        _ai = ai;
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

    // ─── Import ──────────────────────────────────────────────────────────

    // POST /documents/templates/import — multipart, field "file" (.docx,
    // .txt, .md, up to 5 MB). Converts it to template markup for the editor
    // to open unsaved; saving goes through POST /documents/templates. Stores
    // nothing, so a [ReadOnlyAction] like the payroll import previews.
    [RequireScope("documents:write")]
    [ReadOnlyAction]
    [HttpPost("templates/import")]
    [RequestSizeLimit(8 * 1024 * 1024)]   // the 5 MB rule itself is the service's, with a clear 400
    public async Task<IActionResult> ImportTemplate(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Pick a Word (.docx), .txt or .md file to upload." });

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        var result = _templates.Import(file.FileName, buffer.ToArray());
        return result.Ok ? Ok(result.Import) : BadRequest(new { message = result.Error });
    }

    // ─── Write with AI ───────────────────────────────────────────────────

    // POST /documents/templates/ai — the admin describes a letter and gets two
    // drafts to choose from, or (with currentBody) one revision of the draft
    // they're editing. Stores nothing: the draft opens in the editor and is
    // saved through POST /documents/templates, which still refuses unknown
    // fields. People only — each call is a paid AI request — and not a
    // [ReadOnlyAction]: a View-only admin can't save a template, so has no
    // reason to spend AI credit drafting one.
    [RequireScope("documents:write")]
    [HumanOnly]
    [EnableRateLimiting("ai-documents")]
    [HttpPost("templates/ai")]
    public async Task<IActionResult> DraftWithAi(TemplateAiRequestDto dto, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _ai.DraftAsync(dto, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (AiConfigurationException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Writing with AI isn't set up on this server yet." });
        }
        catch (AiProviderException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new { message = "The AI couldn't write the template just now. Please try again." });
        }
    }
}
