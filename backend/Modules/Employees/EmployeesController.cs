using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.LhdnForms;

namespace AltomateHR.Api.Modules.Employees;

[ApiController]
[Route("employees")]
[Authorize(Roles = "Admin,Owner")]   // employee admin is admin/owner only
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employees;
    private readonly IEmployeeImportService _import;
    private readonly IEmployeeProfileService _profiles;
    private readonly IEmployeeDocumentService _documents;
    private readonly ILhdnFormsService _lhdnForms;

    public EmployeesController(
        IEmployeeService employees,
        IEmployeeImportService import,
        IEmployeeProfileService profiles,
        IEmployeeDocumentService documents,
        ILhdnFormsService lhdnForms)
    {
        _employees = employees;
        _import = import;
        _profiles = profiles;
        _documents = documents;
        _lhdnForms = lhdnForms;
    }

    // GET /employees — everyone in the org, with their role + assigned supervisor.
    [RequireScope("employees:read")]
    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _employees.GetAllAsync());

    // GET /employees/{id}/profile — the full HR/statutory profile for one member.
    [RequireScope("employees:read")]
    [HttpGet("{id}/profile")]
    public async Task<IActionResult> GetProfile(string id)
    {
        var profile = await _profiles.GetAsync(id);
        return profile is null ? NotFound() : Ok(profile);   // null → not a member of this org
    }

    // PUT /employees/{id}/profile — upsert that profile (create on first save).
    [HttpPut("{id}/profile")]
    public async Task<IActionResult> SaveProfile(string id, EmployeeProfileDto dto)
    {
        var saved = await _profiles.SaveAsync(id, dto);
        return saved is null ? NotFound() : Ok(saved);
    }

    // POST /employees — add a member to THIS org (the admin's active org). If the
    // email already belongs to a user, that identity is reused (second-org case).
    [HttpPost]
    public async Task<IActionResult> Create(CreateEmployeeDto dto)
    {
        var result = await _employees.CreateAsync(dto);
        return result.Ok ? Ok(result.Employee) : BadRequest(new { message = result.Error });
    }

    // PUT /employees/{id} — set a user's role and/or supervisor.
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateEmployeeDto dto)
    {
        var result = await _employees.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Employee) : BadRequest(new { message = result.Error });
    }

    // GET /employees/{id}/documents — everything attached to this profile.
    [RequireScope("employees:read")]
    [HttpGet("{id}/documents")]
    public async Task<IActionResult> GetDocuments(string id)
    {
        var documents = await _documents.GetAllAsync(id);
        return documents is null ? NotFound() : Ok(documents);
    }

    // POST /employees/{id}/documents — attach a file (ID scan, contract, etc).
    [HttpPost("{id}/documents")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(string id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Pick a file to upload." });

        await using var stream = file.OpenReadStream();
        var result = await _documents.UploadAsync(
            id,
            new EmployeeDocumentUpload(file.FileName, file.ContentType, file.Length, stream));

        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Document) : BadRequest(new { message = result.Error });
    }

    // GET /employees/{id}/documents/{documentId}/download
    [RequireScope("employees:read")]
    [HttpGet("{id}/documents/{documentId}/download")]
    public async Task<IActionResult> DownloadDocument(string id, string documentId)
    {
        var file = await _documents.GetFileAsync(id, documentId);
        if (file is null) return NotFound();

        Response.Headers.CacheControl = "no-store";
        return PhysicalFile(file.Path, file.ContentType, file.DownloadName);
    }

    // DELETE /employees/{id}/documents/{documentId} — unlists it; the physical
    // file is intentionally left on disk (see EmployeeDocumentService.DeleteAsync).
    [HttpDelete("{id}/documents/{documentId}")]
    public async Task<IActionResult> DeleteDocument(string id, string documentId)
    {
        var deleted = await _documents.DeleteAsync(id, documentId);
        return deleted ? NoContent() : NotFound();
    }

    // GET /employees/{id}/lhdn-forms — the card grid: which of the 5 statutory
    // forms are available right now, and why not when they aren't.
    [RequireScope("employees:read")]
    [HttpGet("{id}/lhdn-forms")]
    public async Task<IActionResult> GetLhdnForms(string id)
    {
        var descriptors = await _lhdnForms.GetDescriptorsAsync(id);
        return descriptors is null ? NotFound() : Ok(descriptors);
    }

    // GET /employees/{id}/lhdn-forms/{kind}/download?year=2026
    [RequireScope("employees:read")]
    [HttpGet("{id}/lhdn-forms/{kind}/download")]
    public async Task<IActionResult> DownloadLhdnForm(string id, string kind, [FromQuery] int? year)
    {
        if (!Enum.TryParse<LhdnFormKind>(kind, ignoreCase: true, out var parsedKind))
            return BadRequest(new { message = $"Unknown form kind '{kind}'." });

        var result = await _lhdnForms.GenerateAsync(id, parsedKind, year);
        if (!result.Ok && result.Error is null) return NotFound();
        if (!result.Ok) return BadRequest(new { message = result.Error });

        Response.Headers.CacheControl = "no-store";
        return File(result.Bytes!, "application/pdf", result.FileName);
    }
    // ─── Bulk import ────────────────────────────────────────────────────
    //
    // Creating the account and the membership — the step BEFORE the payroll
    // employees import, which fills in payroll fields for people who already
    // exist and refuses a row it cannot match.
    [HttpGet("import/template")]
    [Authorize(Roles = "Admin,Owner")]
    public IActionResult ImportTemplate([FromQuery] TabularFormat format = TabularFormat.Xlsx)
    {
        var result = _import.BuildTemplate(format);
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpGet("export")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> Export([FromQuery] TabularFormat format = TabularFormat.Xlsx)
    {
        var result = await _import.ExportAsync(format);
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType, result.FileName);
    }

    [HttpPost("import")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> Import(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file was uploaded." });

        var format = Path.GetExtension(file.FileName).ToLowerInvariant() switch
        {
            ".csv" => TabularFormat.Csv,
            _ => TabularFormat.Xlsx,
        };

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);

        var result = await _import.ImportAsync(buffer.ToArray(), format);

        // A whole-file problem is a 400; row errors come back 200 alongside
        // whatever DID import, because the good rows were really applied and
        // the response carries the passwords for the accounts just created.
        // Answering 400 would invite a client that discards the body.
        return result.Message is not null ? BadRequest(new { error = result.Message }) : Ok(result);
    }

}
