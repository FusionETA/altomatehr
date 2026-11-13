using AltomateHR.Api.Modules.DocumentExpiry.Entities;
using AltomateHR.Api.Modules.Employees.Dtos;

namespace AltomateHR.Api.Modules.DocumentExpiry;

// Cross-company reads for the daily reminder run, which has no signed-in
// company: every read names its company explicitly.
public interface IDocumentExpiryRepository
{
    // Active companies with at least one work permit or passport date on file.
    Task<IReadOnlyList<(string Id, string Name)>> GetCompaniesAsync();

    // That company's people with a work permit or passport date.
    Task<IReadOnlyList<IdentityDocumentsDto>> GetDocumentsAsync(string organizationId);

    // Reminders already sent in that company.
    Task<IReadOnlyList<DocumentExpiryReminder>> GetSentAsync(string organizationId);

    Task AddSentAsync(IReadOnlyList<DocumentExpiryReminder> reminders);
}
