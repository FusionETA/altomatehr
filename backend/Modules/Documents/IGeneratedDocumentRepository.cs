using AltomateHR.Api.Modules.Documents.Entities;

namespace AltomateHR.Api.Modules.Documents;

// Data access for letters kept on an employee's file. Tenant-scoped AND
// employee-scoped (a policy-limited admin only sees letters for people in
// their scope) by the global query filter.
public interface IGeneratedDocumentRepository
{
    // Newest first. Null employee = every letter in the company.
    Task<List<GeneratedDocument>> ListAsync(string? employeeUserId, int max);
    Task<GeneratedDocument?> GetByIdAsync(string id);
    Task AddAsync(GeneratedDocument document);
    Task DeleteAsync(GeneratedDocument document);
}
