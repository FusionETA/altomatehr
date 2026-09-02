using System.ComponentModel.DataAnnotations;
using AltomateHR.Api.Common;

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

    public MileageUnit MileageUnit { get; set; } = MileageUnit.KM;

    [Range(10, 100_000)]
    public int GeofenceRadiusMeters { get; set; } = 200;

    // CSV of weekday numbers 1-7 (Mon=1 … Sun=7). Null/blank = Mon-Fri.
    // Leave counts only these days, so a Fri-Mon request costs 2, not 4.
    [MaxLength(20)]
    public string? WorkingDays { get; set; }

    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:MM (24-hour) format")]
    public string WorkingHoursStart { get; set; } = "09:00";

    [Required, RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$", ErrorMessage = "Use HH:MM (24-hour) format")]
    public string WorkingHoursEnd { get; set; } = "18:00";
}
