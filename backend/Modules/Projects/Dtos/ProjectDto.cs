namespace AltomateHR.Api.Modules.Projects.Dtos;

public class ProjectDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? XeroProjectId { get; set; }
    public string? XeroStatus { get; set; }
    public DateTime? XeroSyncedAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? AllowedIps { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
}
