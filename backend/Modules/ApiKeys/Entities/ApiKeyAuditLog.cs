using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.ApiKeys.Entities;

// One row per request authenticated by an ApiKey — traceability for machine traffic.
// NOT tenant-scoped: it's an internal operational log keyed by the api key, written
// AFTER the response so it can record the final status code.
public class ApiKeyAuditLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [MaxLength(40)]
    public string ApiKeyId { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Method { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Path { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    [MaxLength(64)]
    public string? Ip { get; set; }

    public DateTime CreatedAt { get; set; }
}
