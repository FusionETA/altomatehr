namespace AltomateHR.Api.Modules.Organizations;

// Whether a company is deactivated — asked on every request, so the answer is
// cached for a few seconds and dropped the moment a superadmin changes it.
public interface IOrganizationStatus
{
    Task<bool> IsDeactivatedAsync(string organizationId);

    Task<IReadOnlySet<string>> DeactivatedIdsAsync();

    // After deactivating or reactivating, so the change bites at once.
    void Changed();
}
