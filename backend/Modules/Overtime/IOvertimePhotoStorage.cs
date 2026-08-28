namespace AltomateHR.Api.Modules.Overtime;

public interface IOvertimePhotoStorage
{
    Task<OvertimePhotoUploadResult> StoreAsync(OvertimePhotoUpload upload);
    Task<OvertimePhotoFileResult?> GetAsync(string fileName);

    // Best-effort delete. True when the file is gone afterwards (deleted, or
    // never existed); false only on a genuine IO/permission error.
    Task<bool> DeleteAsync(string fileName);
}
