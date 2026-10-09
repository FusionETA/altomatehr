using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Organizations;

// Follows GeneratedDocumentStorage: generated names only, every path segment
// checked so a stored name or org id can never climb out of its folder.
public class OrganizationLogoStorage : IOrganizationLogoStorage
{
    private static readonly HashSet<string> Extensions = new(StringComparer.Ordinal) { ".png", ".jpg", ".webp" };

    private readonly string _root;

    public OrganizationLogoStorage(IWebHostEnvironment environment) =>
        _root = Path.Combine(environment.ContentRootPath, "storage", "org-branding");

    public async Task<string> StoreAsync(string organizationId, byte[] content, string extension)
    {
        if (!Extensions.Contains(extension)) throw new ArgumentException("Unsupported logo type.");
        var directory = DirectoryFor(organizationId)
            ?? throw new ArgumentException("Invalid storage location.");

        var storedFileName =
            $"logo-{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(12).ToLowerInvariant()}{extension}";

        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, storedFileName), content);
        return storedFileName;
    }

    public async Task<byte[]?> ReadAsync(string organizationId, string storedFileName)
    {
        var path = PathFor(organizationId, storedFileName);
        if (path is null || !File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path);
    }

    public void Delete(string organizationId, string storedFileName)
    {
        var path = PathFor(organizationId, storedFileName);
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The row is what counts; an orphaned file is harmless.
        }
    }

    private string? PathFor(string organizationId, string storedFileName)
    {
        if (!IsSafeSegment(storedFileName)) return null;
        var directory = DirectoryFor(organizationId);
        return directory is null ? null : Path.Combine(directory, storedFileName);
    }

    private string? DirectoryFor(string organizationId) =>
        IsSafeSegment(organizationId) ? Path.Combine(_root, organizationId) : null;

    private static bool IsSafeSegment(string? segment) =>
        !string.IsNullOrWhiteSpace(segment)
        && segment != "." && segment != ".."
        && string.Equals(segment, Path.GetFileName(segment), StringComparison.Ordinal)
        && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
