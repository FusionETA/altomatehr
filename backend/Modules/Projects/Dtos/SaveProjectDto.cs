using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Projects.Dtos;

// Used for both create and rename. (Id / IsArchived / org are server-controlled.)
public class SaveProjectDto
{
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    // Street address of the site. Free text: it is shown to people, never
    // parsed or geocoded — the geofence centre below is what the distance
    // check uses.
    [MaxLength(400)]
    public string? Location { get; set; }

    // Optional geofence centre. Both null → not geofenced.
    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }

    // Comma-separated IPs allowed to clock in/out against this project. Only
    // enforced for employees whose policy has RequireIpWhitelist on.
    [MaxLength(1000)]
    public string? AllowedIps { get; set; }

    // Work schedule. Times are local "HH:mm"; WorkingDays is a CSV of ISO
    // weekday numbers (1 = Monday … 7 = Sunday). All null → no schedule.
    [MaxLength(5)] public string? WorkingHoursStart { get; set; }
    [MaxLength(5)] public string? WorkingHoursEnd { get; set; }
    [MaxLength(20)] public string? WorkingDays { get; set; }
    [Range(0, 480)] public int LunchBreakMinutes { get; set; } = 60;

    // Replace-all, both of them: what is sent IS the list afterwards, and an
    // empty array clears it. Per-row endpoints would let the settings screen's
    // single Save button half-apply — three sites saved, the fourth rejected —
    // which for a geofence means an employee locked out of a site that is
    // still on screen.
    public List<SaveGeofencePointDto> GeofencePoints { get; set; } = [];
    public List<SaveAllowedIpDto> AllowedIpEntries { get; set; } = [];
}

public class SaveGeofencePointDto
{
    [Required, MaxLength(160)]
    public string Label { get; set; } = string.Empty;

    [Range(-90, 90)] public double Latitude { get; set; }
    [Range(-180, 180)] public double Longitude { get; set; }
}

public class SaveAllowedIpDto
{
    [Required, MaxLength(160)]
    public string Label { get; set; } = string.Empty;

    // Validated as IPv4 or IPv4 CIDR by the service, not here: the rule lives
    // in IpAllowlist so the matcher and the form can't disagree about what a
    // valid entry is.
    [Required, MaxLength(64)]
    public string Cidr { get; set; } = string.Empty;
}
