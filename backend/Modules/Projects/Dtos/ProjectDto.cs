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
    // Legacy single-value pair and comma-separated string. Both remain as the
    // fallback for a project with no rows in the lists below.
    public string? AllowedIps { get; set; }

    // The geofenced sites, in the order the check walks them — first one
    // inside the radius wins, so this order is behaviour.
    public List<GeofencePointDto> GeofencePoints { get; set; } = [];

    // Labelled allowlist entries. Order carries no meaning: matching asks
    // whether ANY entry covers the address.
    public List<AllowedIpDto> AllowedIpEntries { get; set; } = [];

    // Populated on the LIST endpoint, where the full rows are not: the grid
    // needs to say "3 sites", and reading latitude alone would call a project
    // with three sites "not geofenced".
    public int GeofenceSiteCount { get; set; }
    public int AllowedIpCount { get; set; }
    public string? WorkingHoursStart { get; set; }
    public string? WorkingHoursEnd { get; set; }
    public string? WorkingDays { get; set; }
    public int LunchBreakMinutes { get; set; }
    public bool IsArchived { get; set; }

    // Synced from a Xero tracking category that is no longer the one holding
    // projects. Kept (past claims and shifts point at it) but not offered
    // anywhere a project is picked; see IProjectTrackingScope.
    public bool HiddenByTrackingCategory { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class GeofencePointDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public class AllowedIpDto
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Cidr { get; set; } = string.Empty;
}
