using System.Text.Json;
using AltomateHR.Api.Modules.Employees.Dtos;
using AltomateHR.Api.Modules.Employees.Entities;

namespace AltomateHR.Api.Modules.Employees;

// Admin-managed file attachments (ID scans, contracts, certificates) on an
// employee's profile. Stored as a JSON list on
// EmployeeProfile.PayrollDocumentsJson — matching how the profile's other
// array fields (child relief, fixed allowances) already ride along on that
// record rather than getting their own table.
public class EmployeeDocumentService : IEmployeeDocumentService
{
    private readonly IEmployeeProfileRepository _profiles;
    private readonly IOrganizationMembershipRepository _memberships;
    private readonly IEmployeeDocumentStorage _storage;

    public EmployeeDocumentService(
        IEmployeeProfileRepository profiles,
        IOrganizationMembershipRepository memberships,
        IEmployeeDocumentStorage storage)
    {
        _profiles = profiles;
        _memberships = memberships;
        _storage = storage;
    }

    public async Task<IEnumerable<EmployeeDocumentDto>?> GetAllAsync(string userId)
    {
        if (await _memberships.GetForUserInCurrentOrgAsync(userId) is null)
            return null;

        var records = LoadRecords(await _profiles.GetByUserAsync(userId));
        return records.OrderByDescending(r => r.UploadedAt).Select(ToDto);
    }

    public async Task<(bool Ok, EmployeeDocumentDto? Document, string? Error)> UploadAsync(
        string userId, EmployeeDocumentUpload upload)
    {
        if (await _memberships.GetForUserInCurrentOrgAsync(userId) is null)
            return (false, null, null);

        string storedFileName;
        try
        {
            storedFileName = await _storage.StoreAsync(userId, upload);
        }
        catch (ArgumentException ex)
        {
            return (false, null, ex.Message);
        }

        var record = new EmployeeDocumentRecord
        {
            Id = Guid.NewGuid().ToString(),
            Name = Path.GetFileName(upload.FileName),
            MimeType = upload.ContentType,
            SizeBytes = upload.Length,
            UploadedAt = DateTime.UtcNow,
            StoredFileName = storedFileName,
        };

        var profile = await _profiles.GetByUserAsync(userId);
        var records = LoadRecords(profile);
        records.Add(record);

        if (profile is null)
        {
            profile = new EmployeeProfile { UserId = userId, PayrollDocumentsJson = Serialize(records) };
            await _profiles.AddAsync(profile);   // StampTenant sets OrganizationId
        }
        else
        {
            profile.PayrollDocumentsJson = Serialize(records);
            await _profiles.UpdateAsync(profile);
        }

        return (true, ToDto(record), null);
    }

    public async Task<bool> DeleteAsync(string userId, string documentId)
    {
        if (await _memberships.GetForUserInCurrentOrgAsync(userId) is null)
            return false;

        var profile = await _profiles.GetByUserAsync(userId);
        var records = LoadRecords(profile);
        if (records.RemoveAll(r => r.Id == documentId) == 0)
            return false;

        // The physical file is left on disk — an admin who removes the wrong
        // entry by mistake hasn't destroyed anything, only unlisted it.
        profile!.PayrollDocumentsJson = Serialize(records);
        await _profiles.UpdateAsync(profile);
        return true;
    }

    public async Task<EmployeeDocumentFileResult?> GetFileAsync(string userId, string documentId)
    {
        if (await _memberships.GetForUserInCurrentOrgAsync(userId) is null)
            return null;

        var record = LoadRecords(await _profiles.GetByUserAsync(userId))
            .FirstOrDefault(r => r.Id == documentId);
        if (record is null)
            return null;

        var stored = await _storage.GetAsync(userId, record.StoredFileName);
        // Serve the admin-facing original name, not the generated on-disk one.
        return stored is null ? null : stored with { DownloadName = record.Name };
    }

    private static List<EmployeeDocumentRecord> LoadRecords(EmployeeProfile? profile)
    {
        if (profile?.PayrollDocumentsJson is not { Length: > 0 } json)
            return [];
        try
        {
            return JsonSerializer.Deserialize<List<EmployeeDocumentRecord>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Serialize(List<EmployeeDocumentRecord> records) =>
        JsonSerializer.Serialize(records);

    private static EmployeeDocumentDto ToDto(EmployeeDocumentRecord r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        MimeType = r.MimeType,
        SizeBytes = r.SizeBytes,
        UploadedAt = r.UploadedAt,
    };
}
