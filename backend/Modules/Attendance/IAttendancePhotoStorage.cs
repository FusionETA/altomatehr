namespace AltomateHR.Api.Modules.Attendance;

public interface IAttendancePhotoStorage
{
    Task<AttendancePhotoUploadResult> StoreAsync(AttendancePhotoUpload upload);
    Task<AttendancePhotoFileResult?> GetAsync(string fileName);
}
