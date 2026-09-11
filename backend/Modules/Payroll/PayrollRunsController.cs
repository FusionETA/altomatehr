using AltomateHR.Api.Modules.Payroll.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AltomateHR.Api.Modules.Payroll;

// Payroll runs and the payslips they produce.
//
// Admin-only. An employee's view of their own payslip is a separate, narrower
// surface (phase 8) — these routes return the whole org's pay, so there is no
// employee-facing read here.
[ApiController]
[Route("payroll/runs")]
[Authorize(Roles = "Admin,Owner")]
public class PayrollRunsController : ControllerBase
{
    private readonly IPayrollRunService _runs;
    private readonly IPayrollRunAdjustmentService _adjustments;
    private readonly IPayrollRunClaimService _claims;
    private readonly IStatutoryFileService _statutory;
    private readonly IPayrollXeroSyncService _xero;
    private readonly ISalaryChangeService _salaryChanges;

    public PayrollRunsController(
        IPayrollRunService runs,
        IPayrollRunAdjustmentService adjustments,
        IPayrollRunClaimService claims,
        IStatutoryFileService statutory,
        IPayrollXeroSyncService xero,
        ISalaryChangeService salaryChanges)
    {
        _salaryChanges = salaryChanges;
        _runs = runs;
        _adjustments = adjustments;
        _claims = claims;
        _statutory = statutory;
        _xero = xero;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _runs.GetAllAsync());

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        var detail = await _runs.GetAsync(id);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreatePayrollRunDto dto)
    {
        var result = await _runs.CreateAsync(dto);

        // A period that already has a run is a conflict, not a bad request —
        // the client's fix is to open the existing run, not to resend.
        return result.Ok
            ? CreatedAtAction(nameof(Get), new { id = result.Run!.Id }, result.Run)
            : Conflict(new { error = result.Error });
    }

    // Rebuilds every payslip on the run from the employees' current profiles,
    // discarding the previous ones. Safe to call repeatedly on a draft.
    [HttpPost("{id}/generate")]
    public async Task<IActionResult> Generate(string id)
    {
        var result = await _runs.GenerateAsync(id);

        if (result.Ok) return Ok(result.Result);

        return result.Error is null ? NotFound() : Conflict(new { error = result.Error });
    }

    // ─── The status machine ─────────────────────────────────────────────
    //
    // DRAFT → PENDING_APPROVAL → SUBMITTED, with reject and revert going back.
    // A refused transition is a 409: the run is in a state the caller did not
    // expect, and the fix is to look at it rather than to resend.

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> SubmitForApproval(string id)
    {
        var result = await _runs.SubmitForApprovalAsync(id);

        if (result.Ok) return Ok(result.Run);
        return result.Error is null ? NotFound() : Conflict(new { error = result.Error });
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id)
    {
        var result = await _runs.ApproveAsync(id);

        if (result.Ok) return Ok(result.Run);
        return result.Error is null ? NotFound() : Conflict(new { error = result.Error });
    }

    [HttpPost("{id}/reject")]
    public async Task<IActionResult> Reject(string id, RejectPayrollRunDto dto)
    {
        var result = await _runs.RejectAsync(id, dto.Reason);

        if (result.Ok) return Ok(result.Run);
        return result.Error is null ? NotFound() : Conflict(new { error = result.Error });
    }

    // Reverting cascades to every later submitted month in the same year, so
    // the response reports the whole set, not just the run asked for.
    [HttpPost("{id}/revert")]
    public async Task<IActionResult> Revert(string id)
    {
        var result = await _runs.RevertToDraftAsync(id);

        if (result.Ok) return Ok(new { run = result.Run, alsoReverted = result.AlsoReverted });
        return result.Error is null ? NotFound() : Conflict(new { error = result.Error });
    }

    // What a revert would drag back with it — for the confirm dialog, so the
    // admin is told before rather than after. Empty is the common case.
    [HttpGet("{id}/revert-impact")]
    public async Task<IActionResult> RevertImpact(string id)
    {
        var run = await _runs.GetAsync(id);
        return run is null
            ? NotFound()
            : Ok(new { alsoReverted = await _runs.GetRevertImpactAsync(id) });
    }

    // Deletes the run's payslips, adjustments and claim attachments with it.
    // The claims themselves survive and become free to attach elsewhere.
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var result = await _runs.DeleteDraftAsync(id);

        if (result.Ok) return NoContent();
        return result.Error is null ? NotFound() : Conflict(new { error = result.Error });
    }

    // ─── Statutory files ────────────────────────────────────────────────
    //
    // The monthly submissions. Each is generated on demand from the run's
    // payslips rather than stored — the payslips are the record, a file is
    // just one rendering of them.

    [HttpGet("{id}/files/epf")]
    public Task<IActionResult> EpfCsv(string id) => File(_statutory.RenderEpfCsvAsync(id));

    [HttpGet("{id}/files/socso-eis")]
    public Task<IActionResult> PerkesoTxt(string id) => File(_statutory.RenderPerkesoTxtAsync(id));

    [HttpGet("{id}/files/pcb")]
    public Task<IActionResult> PcbTxt(string id) => File(_statutory.RenderPcbTxtAsync(id));

    // What the files above still need. The run page shows this as a banner,
    // and submission refuses while it is not ok.
    [HttpGet("{id}/readiness")]
    public async Task<IActionResult> Readiness(string id)
    {
        var readiness = await _statutory.GetReadinessAsync(id);

        return readiness is null
            ? NotFound()
            : Ok(new
            {
                ok = readiness.Ok,
                totalMissingCount = readiness.TotalMissingCount,
                orgIssues = readiness.OrgIssues,
                employeeIssues = readiness.EmployeeIssues,
            });
    }

    // ─── Documents ──────────────────────────────────────────────────────

    // One employee's payslip.
    [HttpGet("{id}/documents/payslip/{employeeProfileId}")]
    public Task<IActionResult> Payslip(string id, string employeeProfileId) =>
        File(_statutory.RenderPayslipPdfAsync(id, employeeProfileId));

    // Every payslip as a ZIP of individual PDFs — finance forwards them one
    // at a time, so a single concatenated document would need splitting.
    [HttpGet("{id}/documents/payslips")]
    public Task<IActionResult> AllPayslips(string id) =>
        File(_statutory.RenderAllPayslipsZipAsync(id));

    [HttpGet("{id}/documents/summary")]
    public Task<IActionResult> Summary(string id) =>
        File(_statutory.RenderSummaryPdfAsync(id));

    [HttpGet("{id}/documents/payment-schedule")]
    public Task<IActionResult> PaymentSchedule(string id) =>
        File(_statutory.RenderPaymentSchedulePdfAsync(id));

    // The LHDN MTD §E worksheet, one page per employee.
    [HttpGet("{id}/documents/pcb-details")]
    public Task<IActionResult> PcbDetails(string id) =>
        File(_statutory.RenderPcbDetailsPdfAsync(id));

    // The bank disbursement file. `paymentDate` is the value date; omitted
    // means the last day of the payroll period.
    [HttpGet("{id}/documents/bank-file")]
    public Task<IActionResult> BankFile(string id, [FromQuery] DateTime? paymentDate) =>
        File(_statutory.RenderBankFileAsync(id, paymentDate));

    // A missing employer code or IC is the admin's data to fix, so it is a 409
    // with the specific reason — not a 500, and not a silently truncated file.
    private async Task<IActionResult> File(Task<StatutoryFileResult> render)
    {
        var result = await render;

        if (result.Ok) return base.File(result.Content!, result.ContentType!, result.FileName);

        return result.Error is null ? NotFound() : Conflict(new { error = result.Error });
    }

    // ─── Mid-cycle salary changes ───────────────────────────────────────

    // Where this run's payslips disagree with a salary change that took
    // effect part-way through the month, and the correcting line each one
    // needs. Advisory: the engine pays one salary for the month, and whether
    // a raise was meant to be backdated is the admin's call. Applying a hint
    // means saving its suggested line through the adjustments endpoint.
    [HttpGet("{id}/salary-change-hints")]
    public async Task<IActionResult> SalaryChangeHints(string id)
    {
        var run = await _runs.GetAsync(id);
        return run is null ? NotFound() : Ok(await _salaryChanges.GetHintsForRunAsync(id));
    }

    // ─── Xero ───────────────────────────────────────────────────────────

    // The journal that WOULD post, so an admin can check it against their
    // chart of accounts before anything reaches the ledger. Writes nothing.
    // The org's Xero tracking categories, for the mapping picker in Settings.
    // Not run-scoped, but it lives here with the rest of the payroll-to-Xero
    // surface rather than in a controller of its own.
    [HttpGet("xero/tracking-categories")]
    public async Task<IActionResult> GetXeroTrackingCategories() =>
        Ok(await _xero.GetTrackingCategoriesAsync());

    [HttpGet("{id}/xero/preview")]
    public async Task<IActionResult> XeroPreview(string id)
    {
        var preview = await _xero.PreviewAsync(id);
        return preview is null ? NotFound() : Ok(preview);
    }

    // Post the run. Safe to press twice: the second call reports the journal
    // already there rather than creating another.
    [HttpPost("{id}/xero/sync")]
    public async Task<IActionResult> XeroSync(string id)
    {
        var result = await _xero.SyncAsync(id);

        if (!result.Found) return NotFound();

        // An unmapped account or an unbalanced journal is the admin's
        // configuration to fix, not a server fault — 409, with the reason.
        return result.Ok ? Ok(result) : Conflict(new { error = result.Error });
    }

    // ─── Adjustments ────────────────────────────────────────────────────
    //
    // What an admin types for one employee on one run. These outlive a
    // generation; the payslip line items they produce do not.

    [HttpGet("{id}/adjustments")]
    public async Task<IActionResult> GetAdjustments(string id)
    {
        var run = await _runs.GetAsync(id);
        return run is null ? NotFound() : Ok(await _adjustments.GetForRunAsync(id));
    }

    [HttpGet("{id}/adjustments/{employeeProfileId}")]
    public async Task<IActionResult> GetAdjustment(string id, string employeeProfileId)
    {
        var adjustment = await _adjustments.GetAsync(id, employeeProfileId);
        return adjustment is null ? NotFound() : Ok(adjustment);
    }

    // Everything the adjustment editor needs for one employee, in one read.
    [HttpGet("{id}/adjustments/{employeeProfileId}/context")]
    public async Task<IActionResult> GetAdjustmentContext(string id, string employeeProfileId)
    {
        var context = await _adjustments.GetContextAsync(id, employeeProfileId);
        return context is null ? NotFound() : Ok(context);
    }

    // Replaces the row wholesale — see SavePayrollRunAdjustmentDto for why a
    // partial patch would be ambiguous.
    [HttpPut("{id}/adjustments/{employeeProfileId}")]
    public async Task<IActionResult> SaveAdjustment(
        string id, string employeeProfileId, SavePayrollRunAdjustmentDto dto)
    {
        var result = await _adjustments.SaveAsync(id, employeeProfileId, dto);

        if (!result.Found) return NotFound();

        // A non-draft run is a state conflict; an unknown category is the
        // client's mistake to fix in the payload.
        if (!result.Ok)
        {
            return result.Error!.StartsWith("Unknown adjustment category", StringComparison.Ordinal)
                ? BadRequest(new { error = result.Error })
                : Conflict(new { error = result.Error });
        }

        return Ok(result.Adjustment);
    }

    [HttpDelete("{id}/adjustments/{employeeProfileId}")]
    public async Task<IActionResult> ClearAdjustment(string id, string employeeProfileId)
    {
        var result = await _adjustments.ClearAsync(id, employeeProfileId);

        if (!result.Found) return NotFound();
        return result.Ok ? NoContent() : Conflict(new { error = result.Error });
    }

    // ─── Attached claims ────────────────────────────────────────────────

    [HttpGet("{id}/claims")]
    public async Task<IActionResult> GetClaims(string id)
    {
        var run = await _runs.GetAsync(id);
        return run is null ? NotFound() : Ok(await _claims.GetForRunAsync(id));
    }

    // Claims that could go on a run — org-wide, since a claim is eligible for
    // whichever run an admin chooses to put it on. Rows already attached come
    // back flagged rather than omitted, so the picker can say where they are.
    [HttpGet("{id}/claims/attachable")]
    public async Task<IActionResult> GetAttachableClaims(string id)
    {
        var run = await _runs.GetAsync(id);
        return run is null ? NotFound() : Ok(await _claims.GetAttachableAsync());
    }

    [HttpPost("{id}/claims")]
    public async Task<IActionResult> AttachClaim(string id, AttachPayrollRunClaimDto dto)
    {
        var result = await _claims.AttachAsync(id, dto.ClaimId);

        if (!result.Found) return NotFound();
        return result.Ok ? Ok(result.Attachment) : Conflict(new { error = result.Error });
    }

    [HttpDelete("{id}/claims/{claimId}")]
    public async Task<IActionResult> DetachClaim(string id, string claimId)
    {
        var result = await _claims.DetachAsync(claimId);

        if (!result.Found) return NotFound();
        return result.Ok ? NoContent() : Conflict(new { error = result.Error });
    }
}
