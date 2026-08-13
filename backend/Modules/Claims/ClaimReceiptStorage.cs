using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Claims;

public class ClaimReceiptStorage : IClaimReceiptStorage
{
    private const long MaxReceiptBytes = 8 * 1024 * 1024;
    private const string ReceiptRoutePrefix = "/claims/receipts";

    private static readonly Dictionary<string, string> AllowedContentTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/heic"] = ".heic",
        ["image/heif"] = ".heif",
        ["application/pdf"] = ".pdf",
    };

    private readonly IWebHostEnvironment _environment;

    public ClaimReceiptStorage(IWebHostEnvironment environment) => _environment = environment;

    public async Task<ClaimReceiptUploadResult> StoreAsync(ClaimReceiptUpload upload)
    {
        if (upload.Length <= 0)
            throw new ArgumentException("Receipt file is empty.");

        if (upload.Length > MaxReceiptBytes)
            throw new ArgumentException("Receipt file must be 8 MB or smaller.");

        if (!AllowedContentTypes.TryGetValue(upload.ContentType, out var fallbackExtension))
            throw new ArgumentException("Upload a JPG, PNG, WEBP, HEIC, HEIF, or PDF receipt.");

        var extension = GetSafeExtension(upload.FileName, fallbackExtension);
        var fileName = $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}{extension}";
        var uploadDirectory = GetUploadDirectory();

        Directory.CreateDirectory(uploadDirectory);

        var path = Path.Combine(uploadDirectory, fileName);
        await using var output = File.Create(path);
        await upload.Content.CopyToAsync(output);

        return new ClaimReceiptUploadResult($"{ReceiptRoutePrefix}/{fileName}");
    }

    public Task<ClaimReceiptFileResult?> GetAsync(string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal))
            return Task.FromResult<ClaimReceiptFileResult?>(null);

        var extension = Path.GetExtension(safeFileName).ToLowerInvariant();
        var contentType = AllowedContentTypes.FirstOrDefault(pair => pair.Value == extension).Key;
        if (contentType is null)
            return Task.FromResult<ClaimReceiptFileResult?>(null);

        var path = Path.Combine(GetUploadDirectory(), safeFileName);
        if (!File.Exists(path))
            return Task.FromResult<ClaimReceiptFileResult?>(null);

        return Task.FromResult<ClaimReceiptFileResult?>(
            new ClaimReceiptFileResult(path, contentType, safeFileName));
    }

    private static string GetSafeExtension(string fileName, string fallbackExtension)
    {
        var extension = Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant();
        return AllowedContentTypes.ContainsValue(extension) ? extension : fallbackExtension;
    }

    private string GetUploadDirectory() =>
        Path.Combine(_environment.ContentRootPath, "storage", "receipts");
}
