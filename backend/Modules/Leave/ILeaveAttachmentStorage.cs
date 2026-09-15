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

public sealed record LeaveAttachmentUploadResult(string AttachmentUrl);

public sealed record LeaveAttachmentFileResult(
    string Path,
    string ContentType,
    string DownloadName);
