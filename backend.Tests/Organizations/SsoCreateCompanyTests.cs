using AltomateHR.Api.Modules.Auth;
using AltomateHR.Api.Modules.Organizations;
using AltomateHR.Api.Modules.Organizations.Dtos;

namespace AltomateHR.Api.Tests.Organizations;

// A session that arrived through the SSO hand-off belongs to an account the
// external platform manages. "New company" is hidden in the switcher for it;
// these pin that the server refuses it too, before anything is written.
public class SsoCreateCompanyTests
{
    [Fact]
    public async Task AnSsoSession_CannotCreateACompany()
    {
        // No dependencies: the refusal must come before any of them is touched.
        // Without the guard this would fail on the null repository instead.
        var service = new OrganizationService(null!, null!, null!, null!, null!, null!, null!);

        var ex = await Assert.ThrowsAsync<SsoManagedActionException>(() =>
            service.CreateAsync(new CreateOrganizationDto { Name = "Demo Co Two" }, "usr-admin", viaSso: true));

        Assert.Contains("Altomate", ex.Message);
    }
}
