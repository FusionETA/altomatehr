namespace AltomateHR.Api.Modules.ApiMonitoring.Dtos;

// 4xx and 5xx are kept apart throughout: a 409 is usually the API correctly
// refusing something (a stale draft, a missing IC), a 500 is our bug.
public class ApiEndpointSummaryDto
{
    public string Method { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int Calls { get; set; }
    public int ClientErrors { get; set; }
    public int ServerErrors { get; set; }
    // Share of calls that failed for any reason, 0–1.
    public double ErrorRate { get; set; }
    public int AverageMs { get; set; }
    public int SlowestMs { get; set; }
}

public class ApiMonitoringSummaryDto
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalCalls { get; set; }
    public int TotalClientErrors { get; set; }
    public int TotalServerErrors { get; set; }
    public int AverageMs { get; set; }
    public List<ApiEndpointSummaryDto> Endpoints { get; set; } = [];
    // Every company that called the API in the range, busiest first — ignores
    // the company filter, so it can be the list that filter picks from.
    public List<ApiMonitoringCompanyDto> Companies { get; set; } = [];
}

public class ApiMonitoringCompanyDto
{
    public string OrganizationId { get; set; } = string.Empty;
    public string? OrganizationName { get; set; }
    public int Calls { get; set; }
}

public class ApiRequestErrorDto
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public int DurationMs { get; set; }
    public string? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public string CallerType { get; set; } = string.Empty;
    public string? CallerId { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ExceptionType { get; set; }
    public string? ExceptionSource { get; set; }
}

public class ApiRequestErrorQuery
{
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    // Endpoints are one per method + route, so a route alone would mix
    // GET employees/{id} with PUT and DELETE on the same path.
    public string? Method { get; set; }
    public string? Route { get; set; }
    public string? OrganizationId { get; set; }
    public int? Status { get; set; }
    public int? Limit { get; set; }
}
