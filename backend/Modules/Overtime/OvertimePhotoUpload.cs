namespace AltomateHR.Api.Modules.Overtime;

public sealed record OvertimePhotoUpload(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record OvertimePhotoUploadResult(string PhotoUrl);

public sealed record OvertimePhotoFileResult(
    string Path,
    string ContentType,
    string DownloadName);
