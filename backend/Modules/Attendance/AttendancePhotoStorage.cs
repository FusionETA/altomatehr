using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Attendance;

// Clock-in/out photos — a selfie or a site snapshot, images only (never a PDF,
// unlike a claim receipt).
//
// Xero Files when the org has a connection, local disk otherwise: the same
// order as claim receipts and leave attachments, and the reference app's. The
// fallback is what lets an org with no Xero connection clock in at all, and
// keeps clocking in working on the day Xero is down.
//
// No new columns for this. The four photo fields — clock-in and clock-out, on
// both the record and the session — each hold a url, and a Xero-hosted one
// carries its file id inside that url. The id is recoverable from the url, so
// storing it twice would only create two things that can disagree.
public class AttendancePhotoStorage : IAttendancePhotoStorage
{
    private const long MaxPhotoBytes = 8 * 1024 * 1024;
    private const string PhotoRoutePrefix = "/attendance/photos";

    private static readonly Dictionary<string, string> AllowedContentTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
    };

    // What an accountant sees this grouped under in Xero Files.
    private const string XeroFolder = "Attendance Photos";

    // The url segment that marks a photo as Xero-hosted. Everything after it is
    // the file id.
    public const string XeroSegment = "xero";

    private readonly IWebHostEnvironment _environment;
    private readonly Xero.IXeroFileUploader _xero;

    public AttendancePhotoStorage(IWebHostEnvironment environment, Xero.IXeroFileUploader xero)
    {
        _environment = environment;
        _xero = xero;
    }

    public async Task<AttendancePhotoUploadResult> StoreAsync(AttendancePhotoUpload upload)
    {
        if (upload.Length <= 0)
            throw new ArgumentException("Photo file is empty.");

        if (upload.Length > MaxPhotoBytes)
            throw new ArgumentException("Photo must be 8 MB or smaller.");

        if (!AllowedContentTypes.TryGetValue(upload.ContentType, out var fallbackExtension))
            throw new ArgumentException("Upload a JPG, PNG, WEBP, HEIC, or HEIF photo.");

        var extension = GetSafeExtension(upload.FileName, fallbackExtension);

        // Buffered because both destinations need the whole thing: Xero takes a
        // multipart body, and the local fallback must still be writable after a
        // failed upload has already read the stream. Capped at 8 MB above.
        using var buffer = new MemoryStream();
        await upload.Content.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        var uploaded = await _xero.TryUploadFileAsync(
            XeroFolder, bytes, Path.GetFileName(upload.FileName), upload.ContentType);

        if (uploaded is not null)
            return new AttendancePhotoUploadResult($"{PhotoRoutePrefix}/{XeroSegment}/{uploaded.FileId}");

        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}{extension}";
        var uploadDirectory = GetUploadDirectory();

        Directory.CreateDirectory(uploadDirectory);

        var path = Path.Combine(uploadDirectory, fileName);
        await File.WriteAllBytesAsync(path, bytes);

        return new AttendancePhotoUploadResult($"{PhotoRoutePrefix}/{fileName}");
    }

    public Task<AttendancePhotoFileResult?> GetAsync(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal))
            return Task.FromResult<AttendancePhotoFileResult?>(null);

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        var contentType = AllowedContentTypes.FirstOrDefault(pair => pair.Value == extension).Key;
        if (contentType is null)
            return Task.FromResult<AttendancePhotoFileResult?>(null);

        var path = Path.Combine(GetUploadDirectory(), safeFileName);
        if (!File.Exists(path))
            return Task.FromResult<AttendancePhotoFileResult?>(null);

        return Task.FromResult<AttendancePhotoFileResult?>(
            new AttendancePhotoFileResult(path, contentType, safeFileName));
    }

    public Task<bool> DeleteAsync(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal))
            return Task.FromResult(false);

        var path = Path.Combine(GetUploadDirectory(), safeFileName);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return Task.FromResult(true);
        }
        catch (IOException)
        {
            return Task.FromResult(false);
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(false);
        }
    }

    private static string GetSafeExtension(string fileName, string fallbackExtension)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        return AllowedContentTypes.ContainsValue(extension) ? extension : fallbackExtension;
    }

    private string GetUploadDirectory() =>
        Path.Combine(_environment.ContentRootPath, "storage", "attendance-photos");
}
