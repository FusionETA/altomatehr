namespace AltomateHR.Api.Modules.Claims;

public sealed record ClaimReceiptUpload(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

// XeroFileId is set when the bytes went to Xero Files, null when they went to
// local disk. The caller persists both, so a later read knows which way to go.
public sealed record ClaimReceiptUploadResult(string ReceiptUrl, string? XeroFileId = null);

public sealed record ClaimReceiptFileResult(
    string Path,
    string ContentType,
    string DownloadName);
