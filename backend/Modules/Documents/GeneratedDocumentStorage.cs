using System.Security.Cryptography;

namespace AltomateHR.Api.Modules.Documents;

// Follows EmployeeDocumentStorage: generated names only, every path segment
// checked so a stored name or id can never climb out of its folder.
public class GeneratedDocumentStorage : IGeneratedDocumentStorage
{
    private readonly IWebHostEnvironment _environment;

    public GeneratedDocumentStorage(IWebHostEnvironment environment) => _environment = environment;

    public async Task<string> StoreAsync(string organizationId, string employeeUserId, byte[] content)
    {
        var directory = DirectoryFor(organizationId, employeeUserId)
            ?? throw new ArgumentException("Invalid storage location.");

        var storedFileName =
            $"{DateTime.UtcNow:yyyyMMddHHmmss}-{RandomNumberGenerator.GetHexString(8).ToLowerInvariant()}.pdf";

        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, storedFileName), content);
        return storedFileName;
    }

    public async Task<byte[]?> ReadAsync(string organizationId, string employeeUserId, string storedFileName)
    {
        var path = PathFor(organizationId, employeeUserId, storedFileName);
        if (path is null || !File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path);
    }

    public void Delete(string organizationId, string employeeUserId, string storedFileName)
    {
        var path = PathFor(organizationId, employeeUserId, storedFileName);
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // The record is what the admin sees; an orphaned file is harmless.
        }
    }

    private string? PathFor(string organizationId, string employeeUserId, string storedFileName)
    {
        if (!IsSafeSegment(storedFileName)) return null;
        var directory = DirectoryFor(organizationId, employeeUserId);
        return directory is null ? null : Path.Combine(directory, storedFileName);
    }

    private string? DirectoryFor(string organizationId, string employeeUserId) =>
        IsSafeSegment(organizationId) && IsSafeSegment(employeeUserId)
            ? Path.Combine(_environment.ContentRootPath, "storage", "generated-documents", organizationId, employeeUserId)
            : null;

    private static bool IsSafeSegment(string? segment) =>
        !string.IsNullOrWhiteSpace(segment)
        && segment != "." && segment != ".."
        && string.Equals(segment, Path.GetFileName(segment), StringComparison.Ordinal)
        && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
}
