using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.ApiMonitoring.Entities;

// One row per API request — how the API behaves, not what it was asked.
//
// NOT tenant-scoped, like ApiKeyAuditLog: it is an internal operational log
// read across every company by Fusioneta superadmins only, and rows are saved
// by a background worker with no request context, where the tenant auto-stamp
// would blank the org. OrganizationId is set explicitly from the caller's
// `org` claim instead.
//
// No query string, headers or bodies are kept — only the outcome, and for a
// failure the message the caller was shown.
public class ApiRequestLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public DateTime CreatedAt { get; set; }

    [MaxLength(10)]
    public string Method { get; set; } = string.Empty;

    // The route TEMPLATE ("payroll/runs/{id}/generate"), never the raw path:
    // calls group per endpoint, and record ids stay out of the log.
    [MaxLength(300)]
    public string Route { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public int DurationMs { get; set; }

    [MaxLength(40)]
    public string? OrganizationId { get; set; }

    [MaxLength(20)]
    public string CallerType { get; set; } = ApiCallerTypes.Anonymous;

    [MaxLength(40)]
    public string? CallerId { get; set; }

    [MaxLength(1000)]
    public string? ErrorMessage { get; set; }

    [MaxLength(200)]
    public string? ExceptionType { get; set; }

    [MaxLength(300)]
    public string? ExceptionSource { get; set; }
}

public static class ApiCallerTypes
{
    public const string User = "User";
    public const string ApiKey = "ApiKey";
    public const string Partner = "Partner";
    public const string Anonymous = "Anonymous";
}
