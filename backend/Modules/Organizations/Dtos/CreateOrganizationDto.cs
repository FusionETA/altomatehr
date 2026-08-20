using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Organizations.Dtos;

// What an Owner sends to create a new company. Only the name is required — the
// rest use org defaults (MYR, KM, 200m geofence) and can be edited afterwards.
public class CreateOrganizationDto
{
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;
}
