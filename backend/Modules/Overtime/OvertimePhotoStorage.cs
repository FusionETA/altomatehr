using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Overtime;

public class OvertimePhotoStorage : IOvertimePhotoStorage
{
    private const long MaxPhotoBytes = 8 * 1024 * 1024;
    private const string PhotoRoutePrefix = "/overtime/photos";

    private static readonly Dictionary<string, string> AllowedContentTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
    };

    private readonly IWebHostEnvironment _environment;

    public OvertimePhotoStorage(IWebHostEnvironment environment) => _environment = environment;

    public async Task<OvertimePhotoUploadResult> StoreAsync(OvertimePhotoUpload upload)
    {
        if (upload.Length <= 0)
            throw new ArgumentException("Photo file is empty.");

        if (upload.Length > MaxPhotoBytes)
            throw new ArgumentException("Photo must be 8 MB or smaller.");

        if (!AllowedContentTypes.TryGetValue(upload.ContentType, out var fallbackExtension))
            throw new ArgumentException("Upload a JPG, PNG, WEBP, HEIC, or HEIF photo.");

        var extension = GetSafeExtension(upload.FileName, fallbackExtension);
        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}{extension}";
        var uploadDirectory = GetUploadDirectory();

        Directory.CreateDirectory(uploadDirectory);

        var path = Path.Combine(uploadDirectory, fileName);
        await using var output = File.Create(path);
        await upload.Content.CopyToAsync(output);

        return new OvertimePhotoUploadResult($"{PhotoRoutePrefix}/{fileName}");
    }

    public Task<OvertimePhotoFileResult?> GetAsync(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal))
            return Task.FromResult<OvertimePhotoFileResult?>(null);

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        var contentType = AllowedContentTypes.FirstOrDefault(pair => pair.Value == extension).Key;
        if (contentType is null)
            return Task.FromResult<OvertimePhotoFileResult?>(null);

        var path = Path.Combine(GetUploadDirectory(), safeFileName);
        if (!File.Exists(path))
            return Task.FromResult<OvertimePhotoFileResult?>(null);

        return Task.FromResult<OvertimePhotoFileResult?>(
            new OvertimePhotoFileResult(path, contentType, safeFileName));
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
        Path.Combine(_environment.ContentRootPath, "storage", "overtime-photos");
}
