namespace AltomateHR.Api.Modules.Claims;

public interface IClaimReceiptStorage
{
    Task<ClaimReceiptUploadResult> StoreAsync(ClaimReceiptUpload upload);
    Task<ClaimReceiptFileResult?> GetAsync(string fileName);

    // The bytes of a locally stored receipt, for re-uploading it to Xero.
    // Null when there is no such file.
    Task<ClaimReceiptBytes?> ReadAsync(string fileName);
}
