using AltomateHR.Api.Modules.Dashboard.Dtos;

namespace AltomateHR.Api.Modules.Dashboard;

public interface IAdminOverviewService
{
    // The executive overview for the caller's active org.
    Task<AdminOverviewDto> GetAsync();
}
