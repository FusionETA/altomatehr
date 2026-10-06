using AltomateHR.Api.Modules.ApiMonitoring.Dtos;
using AltomateHR.Api.Modules.ApiMonitoring.Entities;

namespace AltomateHR.Api.Modules.ApiMonitoring;

public interface IApiMonitoringService
{
    // Called by the writer worker with a batch drained from the queue.
    Task RecordBatchAsync(IReadOnlyList<ApiRequestLog> rows);

    // Deletes rows past ApiMonitoringOptions.RetentionDays. Rows deleted.
    Task<int> PurgeExpiredAsync();

    // Defaults to the last 24 hours when no range is given.
    Task<ApiMonitoringSummaryDto> GetSummaryAsync(DateTime? from, DateTime? to, string? organizationId);

    Task<IReadOnlyList<ApiRequestErrorDto>> GetRecentErrorsAsync(ApiRequestErrorQuery query);
}
