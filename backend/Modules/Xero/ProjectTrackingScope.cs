using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Xero;

public class ProjectTrackingScope : IProjectTrackingScope
{
    private readonly IXeroRepository _repo;
    private readonly ICurrentUser _currentUser;

    public ProjectTrackingScope(IXeroRepository repo, ICurrentUser currentUser)
    {
        _repo = repo;
        _currentUser = currentUser;
    }

    public async Task<string?> GetActiveCategoryIdAsync()
    {
        var organizationId = _currentUser.OrganizationId;
        if (string.IsNullOrEmpty(organizationId)) return null;

        var connection = await _repo.GetConnectionAsync(organizationId);
        return connection is { IsConnected: true } ? connection.ProjectTrackingCategoryId : null;
    }
}
