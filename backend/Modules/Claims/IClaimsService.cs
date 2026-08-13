using AltomateHR.Api.Modules.Claims.Dtos;
using AltomateHR.Api.Modules.Claims.Entities;

namespace AltomateHR.Api.Modules.Claims;

public interface IClaimsService
{
    Task<IEnumerable<Claim>> GetAllAsync();
    Task<IEnumerable<Claim>> GetVisibleForUserAsync(string userId, bool isAdmin);
    Task<Claim?> GetByIdAsync(string id);
    Task<Claim?> GetVisibleByIdAsync(string id, string userId, bool isAdmin);
    Task<Claim> CreateAsync(CreateClaimDto dto, string employeeId);
    Task<bool> UpdateAsync(string id, CreateClaimDto dto, string userId, bool isAdmin);
    Task<bool> DeleteAsync(string id);
    Task<ClaimStatusTransitionResult> ApproveAsync(string id);
    Task<ClaimStatusTransitionResult> RejectAsync(string id, string? reviewNotes);
    Task<ClaimReceiptUploadResult> StoreReceiptAsync(ClaimReceiptUpload upload);
    Task<ClaimReceiptFileResult?> GetReceiptForUserAsync(string fileName, string userId, bool isAdmin);
}

public sealed record ClaimStatusTransitionResult(
    bool Found,
    bool Transitioned,
    Claim? Claim,
    string? ErrorMessage = null);
