using AltomateHR.Api.Common.Tabular;
using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Modules.Claims;

public interface IClaimsService
{
    Task<IEnumerable<Claim>> GetMineAsync(string userId);
    Task<IEnumerable<Claim>> GetTeamAsync(string userId);
    Task<Claim?> GetByIdAsync(string id);
    Task<Claim?> GetVisibleByIdAsync(string id, string userId, bool isAdmin);
    Task<Claim> CreateAsync(CreateClaimDto dto, string employeeId);
    Task<Claim?> UpdateAsync(string id, CreateClaimDto dto, string userId, bool isAdmin);
    Task<bool> DeleteAsync(string id);
    Task<ClaimStatusTransitionResult> ApproveAsync(string id, string approverId);

    // Approve many at once, each judged on its own: one claim the caller may not
    // approve never blocks the rest. Over-limit claims are refused here on
    // purpose — see ClaimsService.BulkApproveAsync.
    Task<ClaimsBulkResult> BulkApproveAsync(IReadOnlyList<string> ids, string approverId);

    // Push an approved claim to Xero as a bill. Idempotent: a claim already
    // carrying a XeroBillId is returned untouched rather than billed twice.
    Task<ClaimXeroSyncResult> SyncToXeroAsync(string id);
    Task<ClaimStatusTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes);
    Task<ClaimReceiptUploadResult> StoreReceiptAsync(ClaimReceiptUpload upload);
    Task<ClaimReceiptFileResult?> GetReceiptForUserAsync(string fileName, string userId, bool isAdmin);

    // Every claim in the current org, for cross-module reporting (the admin
    // dashboard). The tenant filter still scopes it.
    Task<IReadOnlyList<Claim>> GetAllForOrgAsync();

    // ---- Import / export ----

    // The claims summary as CSV, XLSX or PDF. Org-wide (admin-gated at the
    // controller); the tenant filter is what keeps it to one org.
    Task<TabularExportResult> ExportSummaryAsync(ClaimsExportQueryDto query, TabularFormat format);

    // The blank import template. CSV or XLSX only — a PDF can't be filled in
    // and uploaded, and the controller refuses it.
    TabularExportResult BuildImportTemplate(TabularFormat format);

    // Bulk-import historical claims. Append-only and idempotent: a row that
    // already exists is skipped, never updated, so a re-upload is safe.
    Task<TabularImportResult> ImportAsync(byte[] content, TabularFormat format);
}

// Mirrors the attendance bulk contract: per-id success/failure, so the response
// is a report rather than a single pass/fail for the whole batch.
// Found=false is a 404; Ok=false with a message is a claim that could not be
// billed and says why. AlreadySynced is called out so a repeat press reads as
// "nothing to do" rather than as an error.
public sealed record ClaimXeroSyncResult(
    bool Found,
    bool Ok,
    Claim? Claim,
    bool AlreadySynced = false,
    string? Error = null);

public sealed record ClaimsBulkResultItem(string Id, bool Ok, string? Error = null);

public sealed record ClaimsBulkResult(int Succeeded, int Failed, IReadOnlyList<ClaimsBulkResultItem> Items);

public sealed record ClaimStatusTransitionResult(
    bool Found,
    bool Transitioned,
    Claim? Claim,
    string? ErrorMessage = null);
