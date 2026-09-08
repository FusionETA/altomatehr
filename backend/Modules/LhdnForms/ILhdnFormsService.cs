using AltomateHR.Api.Modules.LhdnForms.Dtos;

namespace AltomateHR.Api.Modules.LhdnForms;

public interface ILhdnFormsService
{
    // null → not a member of this org (404).
    Task<IEnumerable<LhdnFormDescriptorDto>?> GetDescriptorsAsync(string userId);

    // (false, ..., null) → not a member of this org (404).
    // (false, ..., error) → form not available right now (400) — archive gate.
    Task<(bool Ok, byte[]? Bytes, string? FileName, string? Error)> GenerateAsync(
        string userId, LhdnFormKind kind, int? year);
}
