namespace AltomateHR.Api.Modules.Overtime;

public interface IOvertimePhotoStorage
{
    Task<OvertimePhotoUploadResult> StoreAsync(OvertimePhotoUpload upload);
    Task<OvertimePhotoFileResult?> GetAsync(string fileName);
}
