using AltomateHR.Api.Modules.ApiMonitoring.Entities;

namespace AltomateHR.Api.Modules.ApiMonitoring;

public interface IApiRequestLogRepository
{
    Task AddRangeAsync(IReadOnlyList<ApiRequestLog> rows);

    // Rows deleted.
    Task<int> DeleteOlderThanAsync(DateTime cutoff);

    // One row per method + route in [from, to), optionally one company's only.
    Task<List<ApiEndpointStats>> GetEndpointStatsAsync(DateTime from, DateTime to, string? organizationId);

    // Calls per company in [from, to) — every company, whatever is filtered.
    Task<List<ApiOrganizationCalls>> GetCallsPerOrganizationAsync(DateTime from, DateTime to);

    // Failed calls (status >= 400) in [from, to), newest first.
    Task<List<ApiRequestLog>> GetErrorsAsync(
        DateTime from, DateTime to, string? organizationId, string? route, int? status, int limit);
}

public record ApiOrganizationCalls(string OrganizationId, int Calls);

public record ApiEndpointStats(
    string Method, string Route, int Calls, int ClientErrors, int ServerErrors, long TotalMs, int MaxMs);
