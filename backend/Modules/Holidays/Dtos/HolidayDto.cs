using System.ComponentModel.DataAnnotations;

namespace AltomateHR.Api.Modules.Holidays.Dtos;

public class HolidayDto
{
    public string Id { get; set; } = string.Empty;
    public string? ProjectId { get; set; }        // null = org-wide
    public string Date { get; set; } = string.Empty;   // yyyy-MM-dd
    public string Name { get; set; } = string.Empty;
}

// Create/update a holiday. Id / org are server-controlled.
public class SaveHolidayDto
{
    // Null = org-wide; set = only that project observes it.
    [MaxLength(40)]
    public string? ProjectId { get; set; }

    [Required]
    public DateTime Date { get; set; }

    [Required, MaxLength(160)]
    public string Name { get; set; } = string.Empty;
}

// Which country-year to pull from the public calendar API.
public class ImportHolidaysDto
{
    [Range(2000, 2100)]
    public int Year { get; set; }

    // ISO 3166-1 alpha-2. Defaulted by the client to the org's own country;
    // there is no country on Organization to read it from yet.
    [Required, MaxLength(2), MinLength(2)]
    public string CountryCode { get; set; } = "MY";
}
