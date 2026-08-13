using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Projects.Dtos;

// Used for both create and rename. (Id / IsArchived / org are server-controlled.)
public class SaveProjectDto
{
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    // Optional geofence centre. Both null → not geofenced.
    [Range(-90, 90)]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    public double? Longitude { get; set; }
}
