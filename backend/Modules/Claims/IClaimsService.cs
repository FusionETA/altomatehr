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
    Task<ClaimStatusTransitionResult> RejectAsync(string id, string approverId, string? reviewNotes);
    Task<ClaimReceiptUploadResult> StoreReceiptAsync(ClaimReceiptUpload upload);
    Task<ClaimReceiptFileResult?> GetReceiptForUserAsync(string fileName, string userId, bool isAdmin);

    // ---- Import / export ----

    // The claims summary as CSV or XLSX. Org-wide (admin-gated at the
    // controller); the tenant filter is what keeps it to one org.
    Task<TabularExportResult> ExportSummaryAsync(ClaimsExportQueryDto query, TabularFormat format);

    // The blank import template, in either format, with a worked example row.
    TabularExportResult BuildImportTemplate(TabularFormat format);

    // Bulk-import historical claims. Append-only and idempotent: a row that
    // already exists is skipped, never updated, so a re-upload is safe.
    Task<TabularImportResult> ImportAsync(byte[] content, TabularFormat format);
}

public sealed record ClaimStatusTransitionResult(
    bool Found,
    bool Transitioned,
    Claim? Claim,
    string? ErrorMessage = null);
