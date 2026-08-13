namespace AltomateHR.Api.Modules.Claims;

public interface IClaimReceiptStorage
{
    Task<ClaimReceiptUploadResult> StoreAsync(ClaimReceiptUpload upload);
    Task<ClaimReceiptFileResult?> GetAsync(string fileName);
}
