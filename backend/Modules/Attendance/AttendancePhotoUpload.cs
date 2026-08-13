namespace AltomateHR.Api.Modules.Attendance;

public sealed record AttendancePhotoUpload(
    string FileName,
    string ContentType,
    long Length,
    Stream Content);

public sealed record AttendancePhotoUploadResult(string PhotoUrl);

public sealed record AttendancePhotoFileResult(
    string Path,
    string ContentType,
    string DownloadName);
