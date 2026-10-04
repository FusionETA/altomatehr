using AltomateHR.Api.Data;
using AltomateHR.Api.Modules.ApiMonitoring.Entities;
using Microsoft.EntityFrameworkCore;

namespace AltomateHR.Api.Modules.ApiMonitoring;

public class ApiRequestLogRepository : IApiRequestLogRepository
{
    private readonly AppDbContext _db;

    public ApiRequestLogRepository(AppDbContext db) => _db = db;

    public async Task AddRangeAsync(IReadOnlyList<ApiRequestLog> rows)
    {
        _db.ApiRequestLogs.AddRange(rows);
        await _db.SaveChangesAsync();
    }

    // One statement, not a load-and-remove: a day of traffic can be tens of
    // thousands of rows.
    public Task<int> DeleteOlderThanAsync(DateTime cutoff) =>
        _db.ApiRequestLogs.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync();

    public async Task<List<ApiEndpointStats>> GetEndpointStatsAsync(
        DateTime from, DateTime to, string? organizationId)
    {
        var rows = await InRange(from, to, organizationId)
            .GroupBy(r => new { r.Method, r.Route })
            .Select(g => new
            {
                g.Key.Method,
                g.Key.Route,
                Calls = g.Count(),
                ClientErrors = g.Sum(r => r.StatusCode >= 400 && r.StatusCode < 500 ? 1 : 0),
                ServerErrors = g.Sum(r => r.StatusCode >= 500 ? 1 : 0),
                TotalMs = g.Sum(r => (long)r.DurationMs),
                MaxMs = g.Max(r => r.DurationMs),
            })
            .ToListAsync();

        return rows
            .Select(r => new ApiEndpointStats(
                r.Method, r.Route, r.Calls, r.ClientErrors, r.ServerErrors, r.TotalMs, r.MaxMs))
            .ToList();
    }

    public async Task<List<ApiOrganizationCalls>> GetCallsPerOrganizationAsync(DateTime from, DateTime to)
    {
        var rows = await InRange(from, to, organizationId: null)
            .Where(r => r.OrganizationId != null)
            .GroupBy(r => r.OrganizationId!)
            .Select(g => new { OrganizationId = g.Key, Calls = g.Count() })
            .ToListAsync();

        return rows.Select(r => new ApiOrganizationCalls(r.OrganizationId, r.Calls)).ToList();
    }

    public Task<List<ApiRequestLog>> GetErrorsAsync(
        DateTime from, DateTime to, string? organizationId, string? method, string? route, int? status, int limit)
    {
        var query = InRange(from, to, organizationId).Where(r => r.StatusCode >= 400);
        if (method is not null) query = query.Where(r => r.Method == method);
        if (route is not null) query = query.Where(r => r.Route == route);
        if (status is not null) query = query.Where(r => r.StatusCode == status);

        return query.OrderByDescending(r => r.CreatedAt).Take(limit).ToListAsync();
    }

    private IQueryable<ApiRequestLog> InRange(DateTime from, DateTime to, string? organizationId)
    {
        var query = _db.ApiRequestLogs.Where(r => r.CreatedAt >= from && r.CreatedAt < to);
        return organizationId is null ? query : query.Where(r => r.OrganizationId == organizationId);
    }
}
