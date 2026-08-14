using AltomateHR.Api.Common;

namespace AltomateHR.Api.Modules.Organizations.Dtos;

// What the client sees for an organization (the settings surface).
public class OrganizationDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DefaultCurrency { get; set; } = string.Empty;
    public decimal DefaultMileageRate { get; set; }
    public MileageUnit MileageUnit { get; set; }
    public int GeofenceRadiusMeters { get; set; }
}
