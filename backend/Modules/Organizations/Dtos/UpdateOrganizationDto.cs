using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Organizations.Dtos;

// What an admin sends to update org settings. (Id is never client-set — it comes from the JWT.)
public class UpdateOrganizationDto
{
    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(3, MinimumLength = 3)]
    public string DefaultCurrency { get; set; } = "MYR";

    [Range(0, 100)]
    public decimal DefaultMileageRate { get; set; }

    [Range(10, 100_000)]
    public int GeofenceRadiusMeters { get; set; } = 200;
}
