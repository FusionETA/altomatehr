namespace AltomateHR.Api.Modules.Organizations;

// Where company logos live on disk: storage/org-branding/{organizationId}/
// {storedFileName}, under the content root (the mounted volume that survives
// redeploys). Names are always generated here, never taken from the upload.
public interface IOrganizationLogoStorage
{
    // Writes the image and returns its generated (safe, unique) stored name.
    // `extension` is the one LogoImage read from the bytes (".png" …).
    Task<string> StoreAsync(string organizationId, byte[] content, string extension);

    // Null when the name is unsafe or the file is gone.
    Task<byte[]?> ReadAsync(string organizationId, string storedFileName);

    // Best effort — a file already gone is not an error.
    void Delete(string organizationId, string storedFileName);
}
