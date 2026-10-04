using AltomateHR.Api.Modules.ApiMonitoring.Dtos;
using AltomateHR.Api.Modules.ApiMonitoring.Entities;
using AltomateHR.Api.Modules.Organizations;
using Microsoft.Extensions.Options;

namespace AltomateHR.Api.Modules.ApiMonitoring;

public class ApiMonitoringService : IApiMonitoringService
{
    public const int DefaultErrorLimit = 50;
    public const int MaxErrorLimit = 200;

    private readonly IApiRequestLogRepository _logs;
    private readonly IOrganizationRepository _organizations;
    private readonly ApiMonitoringOptions _options;

    public ApiMonitoringService(
        IApiRequestLogRepository logs,
        IOrganizationRepository organizations,
        IOptions<ApiMonitoringOptions> options)
    {
        _logs = logs;
        _organizations = organizations;
        _options = options.Value;
    }

    public Task RecordBatchAsync(IReadOnlyList<ApiRequestLog> rows) =>
        rows.Count == 0 ? Task.CompletedTask : _logs.AddRangeAsync(rows);

    public Task<int> PurgeExpiredAsync()
    {
        // Floored at one day: a misconfigured 0 must not empty the table every night.
        var days = Math.Max(1, _options.RetentionDays);
        return _logs.DeleteOlderThanAsync(DateTime.UtcNow.AddDays(-days));
    }

    public async Task<ApiMonitoringSummaryDto> GetSummaryAsync(
        DateTime? from, DateTime? to, string? organizationId)
    {
        var (start, end) = Range(from, to);
        var stats = await _logs.GetEndpointStatsAsync(start, end, Blank(organizationId));
        var perCompany = await _logs.GetCallsPerOrganizationAsync(start, end);
        var names = perCompany.Count == 0 ? new Dictionary<string, string>() : await OrganizationNamesAsync();

        var calls = stats.Sum(s => s.Calls);
        return new ApiMonitoringSummaryDto
        {
            Companies = perCompany
                .Select(c => new ApiMonitoringCompanyDto
                {
                    OrganizationId = c.OrganizationId,
                    OrganizationName = names.GetValueOrDefault(c.OrganizationId),
                    Calls = c.Calls,
                })
                .OrderByDescending(c => c.Calls)
                .ThenBy(c => c.OrganizationName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            From = start,
            To = end,
            TotalCalls = calls,
            TotalClientErrors = stats.Sum(s => s.ClientErrors),
            TotalServerErrors = stats.Sum(s => s.ServerErrors),
            AverageMs = calls == 0 ? 0 : (int)Math.Round(stats.Sum(s => (double)s.TotalMs) / calls),
            // Broken first, then busiest: the endpoint to look at is the one
            // throwing 500s, however rarely it is called.
            Endpoints = stats
                .Select(s => new ApiEndpointSummaryDto
                {
                    Method = s.Method,
                    Route = s.Route,
                    Calls = s.Calls,
                    ClientErrors = s.ClientErrors,
                    ServerErrors = s.ServerErrors,
                    ErrorRate = s.Calls == 0 ? 0 : (double)(s.ClientErrors + s.ServerErrors) / s.Calls,
                    AverageMs = s.Calls == 0 ? 0 : (int)Math.Round((double)s.TotalMs / s.Calls),
                    SlowestMs = s.MaxMs,
                })
                .OrderByDescending(e => e.ServerErrors)
                .ThenByDescending(e => e.Calls)
                .ThenBy(e => e.Route, StringComparer.Ordinal)
                .ToList(),
        };
    }

    public async Task<IReadOnlyList<ApiRequestErrorDto>> GetRecentErrorsAsync(ApiRequestErrorQuery query)
    {
        var (start, end) = Range(query.From, query.To);
        var limit = Math.Clamp(query.Limit ?? DefaultErrorLimit, 1, MaxErrorLimit);

        var rows = await _logs.GetErrorsAsync(
            start, end, Blank(query.OrganizationId), Blank(query.Route), query.Status, limit);
        if (rows.Count == 0) return [];

        var names = await OrganizationNamesAsync();

        return rows.Select(r => new ApiRequestErrorDto
        {
            Id = r.Id,
            CreatedAt = r.CreatedAt,
            Method = r.Method,
            Route = r.Route,
            StatusCode = r.StatusCode,
            DurationMs = r.DurationMs,
            OrganizationId = r.OrganizationId,
            OrganizationName = r.OrganizationId is null ? null : names.GetValueOrDefault(r.OrganizationId),
            CallerType = r.CallerType,
            CallerId = r.CallerId,
            ErrorMessage = r.ErrorMessage,
            ExceptionType = r.ExceptionType,
            ExceptionSource = r.ExceptionSource,
        }).ToList();
    }

    // Last 24 hours unless told otherwise. A reversed range is swapped rather
    // than refused — the caller clearly meant the span between the two.
    private static (DateTime From, DateTime To) Range(DateTime? from, DateTime? to)
    {
        var end = to?.ToUniversalTime() ?? DateTime.UtcNow;
        var start = from?.ToUniversalTime() ?? end.AddHours(-24);
        return start <= end ? (start, end) : (end, start);
    }

    private async Task<Dictionary<string, string>> OrganizationNamesAsync() =>
        (await _organizations.GetAllAsync()).ToDictionary(o => o.Id, o => o.Name, StringComparer.Ordinal);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
