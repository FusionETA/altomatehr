using AltomateHR.Api.Modules.Support.Dtos;

namespace AltomateHR.Api.Modules.Support;

// Fusioneta support: every company on the platform, and provisioning a new
// one. Superadmin-only (SUPERADMIN_EMAILS) — enforced at the controller.
// Entering a company is AuthService.EnterSupportAsync; changing its plan is
// OrganizationService.UpdatePlanAsync.
public interface ISupportService
{
    Task<IReadOnlyList<SupportOrganizationDto>> ListOrganizationsAsync(string? search);

    // Throws ArgumentException with a message for input to fix.
    Task<CreateSupportCompanyResultDto> CreateCompanyAsync(CreateSupportCompanyDto dto);
}
