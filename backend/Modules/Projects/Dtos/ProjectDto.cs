namespace AltomateHR.Api.Modules.Projects.Dtos;

public class ProjectDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? XeroProjectId { get; set; }

    // Set instead of XeroProjectId when the project is a Xero tracking-category
    // option. Either one means Xero owns the row, which is what the settings
    // list keys "read-only" off.
    public string? XeroTrackingOptionId { get; set; }
    public string? XeroStatus { get; set; }
    public DateTime? XeroSyncedAt { get; set; }
    public string? Location { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? AllowedIps { get; set; }
    public string? WorkingHoursStart { get; set; }
    public string? WorkingHoursEnd { get; set; }
    public string? WorkingDays { get; set; }
    public int LunchBreakMinutes { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
}
