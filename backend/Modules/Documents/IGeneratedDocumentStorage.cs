namespace AltomateHR.Api.Modules.Documents;

// Where generated letters live on disk:
// storage/generated-documents/{organizationId}/{employeeUserId}/{storedFileName}.
// A separate tree from storage/employee-documents, which holds the files an
// employee can see in their portal.
public interface IGeneratedDocumentStorage
{
    // Writes the PDF and returns the generated (safe, unique) stored name.
    Task<string> StoreAsync(string organizationId, string employeeUserId, byte[] content);

    // Null when the name is unsafe or the file is gone.
    Task<byte[]?> ReadAsync(string organizationId, string employeeUserId, string storedFileName);

    // Best effort — a file already gone is not an error.
    void Delete(string organizationId, string employeeUserId, string storedFileName);
}
