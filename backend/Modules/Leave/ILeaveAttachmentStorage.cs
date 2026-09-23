namespace AltomateHR.Api.Modules.Leave;

public interface ILeaveAttachmentStorage
{
    Task<LeaveAttachmentUploadResult> StoreAsync(LeaveAttachmentUpload upload);
    Task<LeaveAttachmentFileResult?> GetAsync(string fileName);
}

public sealed record LeaveAttachmentUpload(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

// XeroFileId is set when the bytes went to Xero Files; null when they went to
// local disk. The caller stores both, so a later read knows which way to go.
public sealed record LeaveAttachmentUploadResult(string AttachmentUrl, string? XeroFileId = null);

public sealed record LeaveAttachmentFileResult(
    string Path,
    string ContentType,
    string DownloadName);
