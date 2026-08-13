namespace AltomateHR.Api.Modules.Claims;

public sealed record ClaimReceiptUpload(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record ClaimReceiptUploadResult(string ReceiptUrl);

public sealed record ClaimReceiptFileResult(
    string Path,
    string ContentType,
    string DownloadName);
