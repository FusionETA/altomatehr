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
}

public sealed record ClaimStatusTransitionResult(
    bool Found,
    bool Transitioned,
    Claim? Claim,
    string? ErrorMessage = null);
