namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendancePhotoStorage
{
    Task<AttendancePhotoUploadResult> StoreAsync(AttendancePhotoUpload upload);
    Task<AttendancePhotoFileResult?> GetAsync(string fileName);

    // Best-effort delete. Returns true if the file is gone afterwards (deleted,
    // or never existed) — false only on a genuine IO failure.
    Task<bool> DeleteAsync(string fileName);
}
