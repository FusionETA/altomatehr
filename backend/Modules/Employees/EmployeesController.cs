using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Employees.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using AltomateHR.Api.Modules.ApiKeys;
using AltomateHR.Api.Modules.LhdnForms;
using AltomateHR.Api.Modules.Organizations;

namespace AltomateHR.Api.Modules.Employees;

[ApiController]
[Route("employees")]
// A policy-limited admin reaches only their own people; anyone else is 404.
[EmployeeInScope("id")]
[EmployeeInScope("userId")]
[RequireModule(OrgModules.Employees)]
[Authorize(Roles = "Admin,Owner")]   // employee admin is admin/owner only
public class EmployeesController : ControllerBase
{
    private readonly IEmployeeService _employees;
    private readonly IEmployeeImportService _import;
    private readonly IEmployeeProfileService _profiles;
    private readonly IEmployeeDocumentService _documents;
    private readonly ILhdnFormsService _lhdnForms;
    private readonly IEmployeeTransferService _transfers;
    private readonly IEmploymentHistory _history;

    public EmployeesController(
        IEmployeeService employees,
        IEmployeeImportService import,
        IEmployeeProfileService profiles,
        IEmployeeDocumentService documents,
        ILhdnFormsService lhdnForms,
        IEmployeeTransferService transfers,
        IEmploymentHistory history)
    {
        _employees = employees;
        _import = import;
        _profiles = profiles;
        _documents = documents;
        _lhdnForms = lhdnForms;
        _transfers = transfers;
        _history = history;
    }

    // GET /employees — everyone in the org, with their role + assigned supervisor.
    // A lookup other admin screens borrow (claims / attendance filters, the
    // employee form), so it is held to the plan only. See ModuleGrantExempt.
    [ModuleGrantExempt]
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
    [RequireScope("employees:write")]
    [HttpPut("{id}/profile")]
    public async Task<IActionResult> SaveProfile(string id, EmployeeProfileDto dto)
    {
        try
        {
            var saved = await _profiles.SaveAsync(id, dto);
            return saved is null ? NotFound() : Ok(saved);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // POST /employees — add a member to THIS org (the admin's active org). If the
    // email already belongs to a user, that identity is reused (second-org case).
    [RequireScope("employees:write")]
    [HttpPost]
    public async Task<IActionResult> Create(CreateEmployeeDto dto)
    {
        var result = await _employees.CreateAsync(dto);
        return result.Ok ? Ok(result.Employee) : BadRequest(new { error = result.Error });
    }

    // PUT /employees/{id} — set a user's role and/or supervisor.
    [RequireScope("employees:write")]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, UpdateEmployeeDto dto)
    {
        var result = await _employees.UpdateAsync(id, dto);
        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Employee) : BadRequest(new { error = result.Error });
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
    [RequireScope("employees:write")]
    [HttpPost("{id}/documents")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(string id, IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Pick a file to upload." });

        await using var stream = file.OpenReadStream();
        var result = await _documents.UploadAsync(
            id,
            new EmployeeDocumentUpload(file.FileName, file.ContentType, file.Length, stream));

        if (!result.Ok && result.Error is null) return NotFound();
        return result.Ok ? Ok(result.Document) : BadRequest(new { error = result.Error });
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
    [RequireScope("employees:write")]
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
            return BadRequest(new { error = $"Unknown form kind '{kind}'." });

        var result = await _lhdnForms.GenerateAsync(id, parsedKind, year);
        if (!result.Ok && result.Error is null) return NotFound();
        if (!result.Ok) return BadRequest(new { error = result.Error });

        Response.Headers.CacheControl = "no-store";
        return File(result.Bytes!, "application/pdf", result.FileName);
    }
    // GET /employees/{id}/history — every tenure at this company: joins,
    // leaves, transfers in and out, restores. The profile keeps only the
    // current dates; this keeps what they overwrote.
    [RequireScope("employees:read")]
    [HttpGet("{id}/history")]
    public async Task<IActionResult> GetHistory(string id)
    {
        var history = await _history.GetAsync(id);
        return history is null ? NotFound() : Ok(history);
    }

    // ─── Transfer to another company ────────────────────────────────────
    //
    // Moving someone to another company the same admin runs. Human only: the
    // target is chosen from the signed-in admin's OWN memberships, which an
    // org-scoped API key doesn't have.

    // GET /employees/transfers — every queued transfer in this org (list chip).
    [HumanOnly]
    [HttpGet("transfers")]
    public async Task<IActionResult> ListTransfers() => Ok(await _transfers.ListOpenAsync());

    // GET /employees/{userId}/transfer — where they can go + any queued transfer.
    [HumanOnly]
    [HttpGet("{userId}/transfer")]
    public async Task<IActionResult> GetTransfer(string userId)
    {
        var options = await _transfers.GetOptionsAsync(userId);
        return options is null ? NotFound() : Ok(options);
    }

    // POST /employees/{userId}/transfer — schedule; dated today, it runs now.
    [HumanOnly]
    [HttpPost("{userId}/transfer")]
    public async Task<IActionResult> CreateTransfer(string userId, CreateEmployeeTransferDto dto)
    {
        var result = await _transfers.CreateAsync(userId, dto);
        if (result.Ok) return Ok(new { transfer = result.Transfer, executedImmediately = result.ExecutedImmediately });
        return result.Error is null ? NotFound() : BadRequest(new { error = result.Error });
    }

    // POST /employees/{userId}/duplicate — add the same person to another
    // company this admin runs, keeping them here too (concurrent employment).
    [HumanOnly]
    [HttpPost("{userId}/duplicate")]
    public async Task<IActionResult> Duplicate(string userId, DuplicateEmployeeDto dto)
    {
        var (ok, result, error) = await _transfers.DuplicateAsync(userId, dto);
        if (ok) return Ok(result);
        return error is null ? NotFound() : BadRequest(new { error });
    }

    // DELETE /employees/{userId}/transfer/{transferId} — cancel a queued transfer.
    [HumanOnly]
    [HttpDelete("{userId}/transfer/{transferId}")]
    public async Task<IActionResult> CancelTransfer(string userId, string transferId)
    {
        var result = await _transfers.CancelAsync(userId, transferId);
        if (result.Ok) return Ok(result.Transfer);
        return result.Error is null ? NotFound() : BadRequest(new { error = result.Error });
    }

    // ─── Bulk import ────────────────────────────────────────────────────
    //
    // Creating the account and the membership — the step BEFORE the payroll
    // employees import, which fills in payroll fields for people who already
    // exist and refuses a row it cannot match.
    // POST /employees/{userId}/password — set someone's login password.
    //
    // For the employee who can no longer receive the reset code: a returning
    // worker, or one whose personal address is gone. Admin/Owner only, and the
    // service refuses the caller themselves and any Owner account.
    // Never an API key: a key that can set a password can take the account over.
    [HumanOnly]
    [HttpPost("{userId}/password")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> SetPassword(string userId, SetEmployeePasswordDto dto)
    {
        var result = await _employees.SetPasswordAsync(userId, dto.NewPassword);
        if (result.Ok) return NoContent();
        // Error null means the target isn't in this org — 404, not 400, and
        // deliberately not distinguished from "no such user": an admin of one
        // company should not be able to probe for accounts in another.
        return result.Error is null
            ? NotFound()
            : BadRequest(new { error = result.Error });
    }

    [RequireScope("employees:read")]
    [HttpGet("import/template")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> ImportTemplate([FromQuery] TabularFormat format = TabularFormat.Xlsx)
    {
        var result = await _import.BuildTemplateAsync(format);
        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType, result.FileName);
    }

    // GET /employees/export — the whole roster in the import's layout, to edit
    // and re-import. With ?fields=a,b,c only those columns, as a plain table
    // to read or print (Excel or PDF); ?includeArchived=false leaves out
    // archived employees.
    [RequireScope("employees:read")]
    [HttpGet("export")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> Export(
        [FromQuery] TabularFormat format = TabularFormat.Xlsx,
        [FromQuery] string? fields = null,
        [FromQuery] bool includeArchived = true)
    {
        var result = string.IsNullOrWhiteSpace(fields)
            ? await _import.ExportAsync(format)
            : await _import.ExportSelectedAsync(
                format,
                fields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                includeArchived);
        if (result is null) return BadRequest(new { error = "Choose at least one field to export." });

        Response.Headers.CacheControl = "no-store";
        return File(result.Content, result.ContentType, result.FileName);
    }

    // GET /employees/export/fields — what an export can include, grouped.
    [RequireScope("employees:read")]
    [HttpGet("export/fields")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> ExportFields() => Ok(await _import.ExportFieldsAsync());

    [RequireScope("employees:write")]
    [RequireFullEmployeeScope]   // a bulk import matches rows against every employee
    [HttpPost("import")]
    [Authorize(Roles = "Admin,Owner")]
    public async Task<IActionResult> Import(
        IFormFile? file, [FromForm] EmployeeImportBlankCells blankCells = EmployeeImportBlankCells.Keep)
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

        var result = await _import.ImportAsync(buffer.ToArray(), format, blankCells);

        // A whole-file problem is a 400; row errors come back 200 alongside
        // whatever DID import, because the good rows were really applied and
        // the response carries the passwords for the accounts just created.
        // Answering 400 would invite a client that discards the body.
        return result.Message is not null ? BadRequest(new { error = result.Message }) : Ok(result);
    }

}
